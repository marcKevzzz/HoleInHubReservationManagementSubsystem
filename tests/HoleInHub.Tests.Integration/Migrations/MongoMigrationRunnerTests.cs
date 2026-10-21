using FluentAssertions;
using HoleInHub.Data.Entities;
using HoleInHub.Data.Migrations;
using HoleInHub.Data.Migrations.Definitions;
using HoleInHub.Tests.Integration.Fixtures;
using MongoDB.Driver;
using Xunit;

namespace HoleInHub.Tests.Integration.Migrations;

[Trait("Category", "Integration")]
public sealed class MongoMigrationRunnerTests : IClassFixture<MongoDbFixture>
{
    private readonly MongoDbFixture _fixture;

    public MongoMigrationRunnerTests(MongoDbFixture fixture)
    {
        _fixture = fixture;
    }

    private void SkipIfDatabaseUnavailable()
    {
        if (!_fixture.IsServerAvailable)
        {
            throw new InvalidOperationException(
                $"MongoDB integration test environment is unavailable at '{_fixture.ConnectionString}'. " +
                "Please start the replica set with: docker compose -f docker-compose.mongo.yml up -d");
        }
    }

    [Fact]
    public async Task RunAsync_InitialRun_AppliesBaselineMigrationAndHeartbeat()
    {
        SkipIfDatabaseUnavailable();

        // Arrange
        var dbContext = _fixture.CreateDbContext();
        var leaseManager = new MongoMigrationLeaseManager(dbContext);
        var migration = new Migration_001_InitialFoundation();
        var runner = new MongoMigrationRunner(dbContext, leaseManager, [migration]);

        // Act
        var result = await runner.RunAsync();

        // Assert
        result.NewlyApplied.Should().Be(1);
        result.TotalDiscovered.Should().Be(1);

        var applied = await dbContext.Migrations.Find(x => x.MigrationId == migration.MigrationId).FirstOrDefaultAsync();
        applied.Should().NotBeNull();
        applied.Checksum.Should().Be(migration.CalculateChecksum());

        var heartbeat = await dbContext.SystemHeartbeats.Find(x => x.Id == SystemHeartbeat.DefaultId).FirstOrDefaultAsync();
        heartbeat.Should().NotBeNull();
    }

    [Fact]
    public async Task RunAsync_ExecutedTwice_IsIdempotentAndAppliesZeroNewMigrations()
    {
        SkipIfDatabaseUnavailable();

        // Arrange
        var dbContext = _fixture.CreateDbContext();
        var leaseManager = new MongoMigrationLeaseManager(dbContext);
        var migration = new Migration_001_InitialFoundation();
        var runner = new MongoMigrationRunner(dbContext, leaseManager, [migration]);

        // Act
        var firstResult = await runner.RunAsync();
        var secondResult = await runner.RunAsync();

        // Assert
        firstResult.NewlyApplied.Should().Be(1);
        secondResult.NewlyApplied.Should().Be(0);
        secondResult.AlreadyApplied.Should().Be(1);
    }

    [Fact]
    public async Task RunAsync_WithChecksumTampered_ThrowsMigrationChecksumMismatchException()
    {
        SkipIfDatabaseUnavailable();

        // Arrange
        var dbContext = _fixture.CreateDbContext();
        var leaseManager = new MongoMigrationLeaseManager(dbContext);
        var migration = new Migration_001_InitialFoundation();
        var runner = new MongoMigrationRunner(dbContext, leaseManager, [migration]);

        await runner.RunAsync();

        // Tamper with checksum in database
        var update = Builders<MigrationRecord>.Update.Set(x => x.Checksum, "tampered_bad_checksum_hash");
        await dbContext.Migrations.UpdateOneAsync(x => x.MigrationId == migration.MigrationId, update);

        // Act
        var act = async () => await runner.RunAsync();

        // Assert
        await act.Should().ThrowAsync<MigrationChecksumMismatchException>()
            .WithMessage($"*{migration.MigrationId}*checksum mismatch*");
    }

    [Fact]
    public async Task RunAsync_LongMigration_KeepsLeaseBeyondOriginalExpiry()
    {
        SkipIfDatabaseUnavailable();
        var context = _fixture.CreateDbContext();
        var leases = new MongoMigrationLeaseManager(context);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var migration = new TestMigration(async ct =>
        {
            started.SetResult();
            await finish.Task.WaitAsync(ct);
        });
        var runner = new MongoMigrationRunner(context, leases, [migration]);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var execution = runner.RunAsync("first", TimeSpan.FromSeconds(3), deadline.Token);
        try
        {
            await started.Task.WaitAsync(deadline.Token);
            await Task.Delay(TimeSpan.FromSeconds(5), deadline.Token);
            var acquired = await leases.TryAcquireLeaseAsync("second", TimeSpan.FromSeconds(3), deadline.Token);
            acquired.Should().BeFalse("the first migration is still running beyond its original lease");
        }
        finally
        {
            finish.TrySetResult();
            await execution;
        }
        var released = await leases.TryAcquireLeaseAsync("second", TimeSpan.FromSeconds(3), deadline.Token);
        released.Should().BeTrue("the heartbeat must stop and release its lease on completion");
    }

    [Fact]
    public async Task RunAsync_LeaseLost_CancelsMigrationWithoutRecordingSuccess()
    {
        SkipIfDatabaseUnavailable();
        var context = _fixture.CreateDbContext();
        var leases = new MongoMigrationLeaseManager(context);
        var cancelled = false;
        var migration = new TestMigration(async ct =>
        {
            await context.MigrationLeases.UpdateOneAsync(
                x => x.LeaseId == MigrationLease.DefaultLeaseId,
                Builders<MigrationLease>.Update.Set(x => x.Owner, "replacement"), cancellationToken: ct);
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                cancelled = true;
                throw;
            }
        });
        var runner = new MongoMigrationRunner(context, leases, [migration]);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var act = () => runner.RunAsync("original", TimeSpan.FromSeconds(3), deadline.Token);

        await act.Should().ThrowAsync<MigrationLeaseLostException>();
        cancelled.Should().BeTrue();
        (await context.Migrations.CountDocumentsAsync(x => x.MigrationId == migration.MigrationId)).Should().Be(0);
        var lease = await context.MigrationLeases.Find(x => x.LeaseId == MigrationLease.DefaultLeaseId).SingleAsync();
        lease.Owner.Should().Be("replacement");
        lease.ExpiresAtUtc.Should().BeAfter(DateTime.UtcNow);
    }

    private sealed class TestMigration(Func<CancellationToken, Task> execute) : IMongoMigration
    {
        public string MigrationId => "test_heartbeat";
        public string Description => "Exercise lease renewal";
        public string CalculateChecksum() => "test_heartbeat_v1";
        public Task UpAsync(IMongoDatabase database, CancellationToken ct) => execute(ct);
    }

    [Fact]
    public async Task TryAcquireLeaseAsync_WhenAlreadyHeldByActiveRunner_ReturnsFalse()
    {
        SkipIfDatabaseUnavailable();

        // Arrange
        var dbContext = _fixture.CreateDbContext();
        var leaseManager = new MongoMigrationLeaseManager(dbContext);

        const string runnerA = "runner-A";
        const string runnerB = "runner-B";

        // Act
        var acquiredA = await leaseManager.TryAcquireLeaseAsync(runnerA, TimeSpan.FromMinutes(2));
        var acquiredB = await leaseManager.TryAcquireLeaseAsync(runnerB, TimeSpan.FromMinutes(2));

        // Clean up
        await leaseManager.ReleaseLeaseAsync(runnerA);

        // Assert
        acquiredA.Should().BeTrue();
        acquiredB.Should().BeFalse();
    }
}
