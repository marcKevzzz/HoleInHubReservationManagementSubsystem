using System.Diagnostics;
using HoleInHub.Data.Context;
using HoleInHub.Data.Entities;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace HoleInHub.Data.Migrations;

public sealed class MigrationChecksumMismatchException(string migrationId, string expectedChecksum, string actualChecksum)
    : Exception($"Migration '{migrationId}' checksum mismatch! Expected: {expectedChecksum}, Found: {actualChecksum}. Migrations are immutable and cannot be altered once applied.");

public sealed class MigrationExecutionException(string migrationId, Exception innerException)
    : Exception($"Migration '{migrationId}' failed to execute: {innerException.Message}", innerException);

public sealed record MigrationRunnerResult(
    int TotalDiscovered,
    int AlreadyApplied,
    int NewlyApplied,
    TimeSpan Elapsed);

public sealed class MongoMigrationRunner(
    IMongoDbContext dbContext,
    MongoMigrationLeaseManager leaseManager,
    IEnumerable<IMongoMigration> migrations,
    ILogger<MongoMigrationRunner>? logger = null)
{
    private readonly IMongoDbContext _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    private readonly MongoMigrationLeaseManager _leaseManager = leaseManager ?? throw new ArgumentNullException(nameof(leaseManager));
    private readonly IReadOnlyList<IMongoMigration> _migrations = (migrations ?? Enumerable.Empty<IMongoMigration>())
        .OrderBy(m => m.MigrationId, StringComparer.Ordinal)
        .ToList();
    private readonly ILogger<MongoMigrationRunner>? _logger = logger;

    public async Task<MigrationRunnerResult> RunAsync(
        string? runnerId = null,
        TimeSpan? leaseTimeout = null,
        CancellationToken ct = default)
    {
        runnerId ??= $"runner-{Environment.MachineName}-{Guid.NewGuid():N}";
        var timeout = leaseTimeout ?? TimeSpan.FromSeconds(60);
        if (timeout < TimeSpan.FromMilliseconds(3) || timeout.TotalMilliseconds / 3 > uint.MaxValue - 1)
        {
            throw new ArgumentOutOfRangeException(nameof(leaseTimeout), "Lease duration must support a positive heartbeat interval.");
        }

        _logger?.LogInformation("Attempting to acquire migration lease for runner '{RunnerId}'...", runnerId);

        var acquired = await _leaseManager.TryAcquireLeaseAsync(runnerId, timeout, ct);
        if (!acquired)
        {
            throw new MigrationLeaseException(
                "Failed to acquire exclusive migration lease. Another runner is currently executing migrations.");
        }

        var overallSw = Stopwatch.StartNew();
        var newlyApplied = 0;
        var alreadyAppliedCount = 0;
        using var migrationCancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
        using var heartbeatStop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var heartbeat = MaintainLeaseAsync(runnerId, timeout, migrationCancellation, heartbeatStop.Token);
        ct = migrationCancellation.Token;

        try
        {
            _logger?.LogInformation("Exclusive migration lease acquired. Querying applied migrations...");

            var appliedRecords = await _dbContext.Migrations
                .Find(FilterDefinition<MigrationRecord>.Empty)
                .ToListAsync(ct);

            var appliedMap = appliedRecords.ToDictionary(r => r.MigrationId, r => r);
            alreadyAppliedCount = appliedMap.Count;

            // 1. Verify checksums of all previously applied migrations
            foreach (var migration in _migrations)
            {
                if (appliedMap.TryGetValue(migration.MigrationId, out var record))
                {
                    var expectedChecksum = migration.CalculateChecksum();
                    if (!string.Equals(record.Checksum, expectedChecksum, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new MigrationChecksumMismatchException(
                            migration.MigrationId,
                            expectedChecksum,
                            record.Checksum);
                    }
                }
            }

            // 2. Execute pending migrations in deterministic order
            foreach (var migration in _migrations)
            {
                if (appliedMap.ContainsKey(migration.MigrationId))
                {
                    _logger?.LogDebug("Skipping already applied migration: {MigrationId}", migration.MigrationId);
                    continue;
                }

                _logger?.LogInformation("Applying migration: {MigrationId} - {Description}", migration.MigrationId, migration.Description);

                // Renew lease before executing migration
                await _leaseManager.RenewLeaseAsync(runnerId, timeout, ct);

                var stepSw = Stopwatch.StartNew();
                try
                {
                    await migration.UpAsync(_dbContext.Database, ct);
                    ct.ThrowIfCancellationRequested();
                    await _leaseManager.RenewLeaseAsync(runnerId, timeout, ct);
                    stepSw.Stop();

                    var record = new MigrationRecord
                    {
                        MigrationId = migration.MigrationId,
                        Description = migration.Description,
                        Checksum = migration.CalculateChecksum(),
                        AppliedAtUtc = DateTime.UtcNow,
                        ExecutionDurationMs = stepSw.ElapsedMilliseconds
                    };

                    await _dbContext.Migrations.InsertOneAsync(record, cancellationToken: ct);
                    newlyApplied++;

                    _logger?.LogInformation(
                        "Successfully applied migration: {MigrationId} ({DurationMs}ms)",
                        migration.MigrationId,
                        stepSw.ElapsedMilliseconds);
                }
                catch (Exception ex) when (ex is not MigrationLeaseLostException and not OperationCanceledException)
                {
                    _logger?.LogError(ex, "Failed to apply migration: {MigrationId}", migration.MigrationId);
                    throw new MigrationExecutionException(migration.MigrationId, ex);
                }
            }

            overallSw.Stop();
            _logger?.LogInformation(
                "Migration run complete. Total: {Total}, Applied: {Applied}, Newly Applied: {NewlyApplied} in {ElapsedMs}ms",
                _migrations.Count,
                alreadyAppliedCount,
                newlyApplied,
                overallSw.ElapsedMilliseconds);

            return new MigrationRunnerResult(
                _migrations.Count,
                alreadyAppliedCount,
                newlyApplied,
                overallSw.Elapsed);
        }
        finally
        {
            await heartbeatStop.CancelAsync();
            try
            {
                await heartbeat;
            }
            finally
            {
                try
                {
                    await _leaseManager.ReleaseLeaseAsync(runnerId, CancellationToken.None);
                    _logger?.LogInformation("Released migration lease for runner '{RunnerId}'.", runnerId);
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Failed to cleanly release migration lease for runner '{RunnerId}'.", runnerId);
                }
            }
        }
    }

    private async Task MaintainLeaseAsync(
        string runnerId,
        TimeSpan timeout,
        CancellationTokenSource migrationCancellation,
        CancellationToken ct)
    {
        using var timer = new PeriodicTimer(timeout / 3);
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                // A stalled renewal must cancel work before the previous lease can expire.
                using var renewalTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                renewalTimeout.CancelAfter(timeout / 3);
                await _leaseManager.RenewLeaseAsync(runnerId, timeout, renewalTimeout.Token);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The caller or the completed run stopped the heartbeat.
        }
        catch (Exception ex)
        {
            await migrationCancellation.CancelAsync();
            throw new MigrationLeaseLostException("Migration heartbeat failed; execution was cancelled.", ex);
        }
    }
}
