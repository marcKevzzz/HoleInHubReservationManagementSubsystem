
using HoleInHub.Business.Accounts;
using HoleInHub.Data.Accounts;
using HoleInHub.Data.Extensions;
using HoleInHub.Data.Migrations;
using HoleInHub.Data.Migrations.Definitions;
using HoleInHub.Tests.Integration.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Driver;
using Xunit;

namespace HoleInHub.Tests.Integration.Accounts;

[Trait("Category", "Integration")]
public sealed class StaffRoleChangeTests(MongoDbFixture fixture) : IClassFixture<MongoDbFixture>
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MigrationCreatesCollectionsAndRerunPreservesExistingData(bool upgrading)
    {
        var db = fixture.CreateDbContext();
        var accounts = db.Database.GetCollection<AccountRecord>("accounts");
        var existing = Account(StaffRoles.Admin);
        var migration = new Migration_017_StaffRoleAdministration();
        if (upgrading)
        {
            var earlierMigrations = new ServiceCollection().AddHoleInHubMigrations()
                .Where(service => service.ServiceType == typeof(IMongoMigration))
                .Select(service => (IMongoMigration)Activator.CreateInstance(service.ImplementationType!)!)
                .Where(item => string.CompareOrdinal(item.MigrationId, migration.MigrationId) < 0).ToArray();
            var earlier = new MongoMigrationRunner(db, new MongoMigrationLeaseManager(db), earlierMigrations);
            Assert.Equal(16, (await earlier.RunAsync()).NewlyApplied);
            await accounts.InsertOneAsync(existing);
        }

        var runner = new MongoMigrationRunner(db, new MongoMigrationLeaseManager(db), [migration]);
        Assert.Equal(1, (await runner.RunAsync()).NewlyApplied);
        var audit = db.Database.GetCollection<BsonDocument>("staff_role_audit");
        var guards = db.Database.GetCollection<BsonDocument>("staff_role_guards");
        var guardId = new BsonDocument("_id", "staff-role-administration");
        Assert.Equal(1, await guards.CountDocumentsAsync(guardId));
        await guards.UpdateOneAsync(guardId, new BsonDocument("$inc", new BsonDocument("version", 1)));
        await audit.InsertOneAsync(new BsonDocument("_id", "existing-audit"));
        await migration.UpAsync(db.Database, default);
        Assert.Equal(1, (await guards.Find(guardId).SingleAsync())["version"].ToInt32());
        Assert.Equal(1, await audit.CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty));
        Assert.Equal(upgrading ? 1 : 0, await accounts.CountDocumentsAsync(FilterDefinition<AccountRecord>.Empty));
        if (upgrading)
        {
            var saved = await accounts.Find(x => x.Id == existing.Id).SingleAsync();
            Assert.Equal(existing.Roles, saved.Roles);
            Assert.Equal(existing.SecurityStamp, saved.SecurityStamp);
        }
    }

    [Fact]
    public async Task GrantPromoteAndRevokePreserveCustomerDataAndRejectReplayAndOldProfileWrites()
    {
        var (persistence, service, audit) = await SetUpAsync();
        var actor = Account(StaffRoles.Admin);
        var target = Account("Customer", "LegacyRole");
        await persistence.CreateAsync(actor, default);
        await persistence.CreateAsync(target, default);

        var grant = Command(actor, target, StaffRoles.Staff);
        Assert.Equal(StaffRoleChangeResult.Applied, await service.ChangeStaffRoleAsync(grant, default));
        Assert.Equal(StaffRoleChangeResult.Stale, await service.ChangeStaffRoleAsync(grant, default));
        Assert.False(await persistence.UpdateAsync(target, target.Version, default));

        foreach (var role in new[] { StaffRoles.Manager, StaffRoles.Admin, StaffRoles.Staff })
        {
            var saved = (await persistence.FindByIdAsync(target.Id, default))!;
            Assert.Equal(StaffRoleChangeResult.Applied, await service.ChangeStaffRoleAsync(Command(actor, saved, role), default));
        }
        var current = (await persistence.FindByIdAsync(target.Id, default))!;
        Assert.Equal(StaffRoleChangeResult.Unchanged,
            await service.ChangeStaffRoleAsync(Command(actor, current, StaffRoles.Staff), default));
        Assert.Equal(StaffRoleChangeResult.Applied, await service.ChangeStaffRoleAsync(Command(actor, current, null), default));
        var revoked = (await persistence.FindByIdAsync(target.Id, default))!;
        Assert.Equal(target.Roles, revoked.Roles);
        Assert.Equal(target.Email, revoked.Email);
        Assert.Equal(target.PasswordHash, revoked.PasswordHash);
        Assert.NotEqual(target.SecurityStamp, revoked.SecurityStamp);
        var events = await audit.Find(FilterDefinition<BsonDocument>.Empty).ToListAsync();
        Assert.Equal(5, events.Count);
        Assert.All(events, record =>
        {
            Assert.Equal(actor.Id, record["ActorAccountId"].AsString);
            Assert.Equal(target.Id, record["TargetAccountId"].AsString);
            Assert.Equal("Roster change", record["Reason"].AsString);
            Assert.False(record.Contains("SecurityStamp"));
        });
    }

    [Fact]
    public async Task MutualDemotionLeavesOneAdminAndOneAuditEvent()
    {
        var (persistence, service, audit) = await SetUpAsync();
        var first = Account(StaffRoles.Admin);
        var second = Account(StaffRoles.Admin);
        await persistence.CreateAsync(first, default);
        await persistence.CreateAsync(second, default);

        var results = await Task.WhenAll(
            service.ChangeStaffRoleAsync(Command(first, second, StaffRoles.Staff), default),
            service.ChangeStaffRoleAsync(Command(second, first, StaffRoles.Staff), default));

        Assert.Contains(StaffRoleChangeResult.Applied, results);
        Assert.Contains(StaffRoleChangeResult.Forbidden, results);
        var directory = await persistence.ReadDirectoryAsync([StaffRoles.Admin], 0, 5, default);
        Assert.Single(directory);
        Assert.Equal(1, await audit.CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty));
    }

    [Fact]
    public async Task AuditFailureRollsBackTheAccountWrite()
    {
        var db = fixture.CreateDbContext();
        await new Migration_017_StaffRoleAdministration().UpAsync(db.Database, default);
        await db.Database.RunCommandAsync<BsonDocument>(new BsonDocument
        {
            { "collMod", "staff_role_audit" },
            { "validator", new BsonDocument("TestRejectsAllRoleEvents", true) }
        });
        var persistence = new AccountPersistence(db);
        var service = new AccountService(persistence, TimeProvider.System);
        var actor = Account(StaffRoles.Admin);
        var target = Account(StaffRoles.Staff);
        await persistence.CreateAsync(actor, default);
        await persistence.CreateAsync(target, default);

        await Assert.ThrowsAsync<MongoWriteException>(() => service.ChangeStaffRoleAsync(Command(actor, target, null), default));

        var saved = (await persistence.FindByIdAsync(target.Id, default))!;
        Assert.Equal(target.Roles, saved.Roles);
        Assert.Equal(target.Version, saved.Version);
        Assert.Equal(target.SecurityStamp, saved.SecurityStamp);
    }

    [Theory]
    [InlineData(false, "hash")]
    [InlineData(true, "")]
    public async Task IneligibleAccountsCannotGainStaffAccessButCanBeRevoked(bool verified, string passwordHash)
    {
        var (persistence, service, audit) = await SetUpAsync();
        var actor = Account(StaffRoles.Admin);
        var target = Account(StaffRoles.Staff);
        target.EmailConfirmed = verified;
        target.PasswordHash = passwordHash;
        await persistence.CreateAsync(actor, default);
        await persistence.CreateAsync(target, default);

        Assert.Equal(StaffRoleChangeResult.Invalid,
            await service.ChangeStaffRoleAsync(Command(actor, target, StaffRoles.Manager), default));
        Assert.Equal(StaffRoleChangeResult.Forbidden,
            await service.ChangeStaffRoleAsync(Command(actor, target, null) with { ActorStamp = "old-stamp" }, default));
        Assert.Equal(StaffRoleChangeResult.Applied,
            await service.ChangeStaffRoleAsync(Command(actor, target, null), default));
        Assert.Equal(1, await audit.CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty));
    }

    [Fact]
    public async Task ConcurrentChangesWithTheSameVersionHaveOneWinner()
    {
        var (persistence, service, audit) = await SetUpAsync();
        var actor = Account(StaffRoles.Admin);
        var target = Account("Customer");
        await persistence.CreateAsync(actor, default);
        await persistence.CreateAsync(target, default);

        var results = await Task.WhenAll(
            service.ChangeStaffRoleAsync(Command(actor, target, StaffRoles.Staff), default),
            service.ChangeStaffRoleAsync(Command(actor, target, StaffRoles.Manager), default));

        Assert.Contains(StaffRoleChangeResult.Applied, results);
        Assert.Contains(StaffRoleChangeResult.Stale, results);
        Assert.Equal(1, await audit.CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty));
    }

    [Fact]
    public async Task PinSetupUsingAnOldStampCannotOverwriteANewlyEnrolledPin()
    {
        var db = fixture.CreateDbContext();
        await new Migration_017_StaffRoleAdministration().UpAsync(db.Database, default);
        var accounts = new AccountPersistence(db);
        var pins = new StaffPinPersistence(db);
        var service = new AccountService(accounts, TimeProvider.System);
        var actor = Account(StaffRoles.Admin);
        var employee = Account(StaffRoles.Staff);
        await accounts.CreateAsync(actor, default);
        await accounts.CreateAsync(employee, default);
        Assert.Equal(StaffRoleChangeResult.Applied,
            await service.ChangeStaffRoleAsync(Command(actor, employee, StaffRoles.Manager), default));

        var promoted = (await accounts.FindByIdAsync(employee.Id, default))!;
        var fresh = new StaffPinCredential(employee.Id, "new-hash", "new-version");
        Assert.True(await pins.TrySaveAsync(fresh, promoted.SecurityStamp, StaffRoles.Workspace, default));
        Assert.False(await pins.TrySaveAsync(new(employee.Id, "old-hash", "old-version"),
            employee.SecurityStamp, StaffRoles.Workspace, default));
        Assert.Equal(fresh, await pins.ReadAsync(employee.Id, default));
    }

    [Fact]
    public async Task ActivityHistoryPagesConsistentlyAndKeepsMissingAccountIds()
    {
        var (persistence, service, audit) = await SetUpAsync();
        var actor = Account(StaffRoles.Admin);
        await persistence.CreateAsync(actor, default);
        var occurred = DateTime.UtcNow;
        var records = Enumerable.Range(0, 51).Select(index => new StaffRoleAuditRecord(
            $"event-{index:D2}", "StaffRoleChanged", occurred, actor.Id, StaffRoles.Admin, "missing-account",
            [], [StaffRoles.Staff], $"Change {index}", "walkthrough").ToBsonDocument());
        await audit.InsertManyAsync(records);

        var first = await service.GetRoleActivityAsync(1, default);
        Assert.Equal(20, first.Entries.Count);
        Assert.True(first.HasMore);
        Assert.Equal("Change 50", first.Entries[0].Reason);
        Assert.Equal(actor.Email, first.Entries[0].Actor);
        Assert.Equal("missing-account", first.Entries[0].Target);

        var second = await service.GetRoleActivityAsync(2, default);
        Assert.True(second.HasMore);
        Assert.Equal(20, second.Entries.Count);
        var third = await service.GetRoleActivityAsync(3, default);
        Assert.False(third.HasMore);
        Assert.Equal(11, third.Entries.Count);
        Assert.Equal("Change 0", third.Entries.Last().Reason);
    }

    private async Task<(AccountPersistence, AccountService, IMongoCollection<BsonDocument>)> SetUpAsync()
    {
        var db = fixture.CreateDbContext();
        await new Migration_017_StaffRoleAdministration().UpAsync(db.Database, default);
        var persistence = new AccountPersistence(db);
        return (persistence, new AccountService(persistence, TimeProvider.System),
            db.Database.GetCollection<BsonDocument>("staff_role_audit"));
    }

    private static AccountRecord Account(params string[] roles)
    {
        var email = $"{Guid.NewGuid():N}@example.test";
        return new AccountRecord
        {
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            EmailConfirmed = true,
            PasswordHash = "test-hash",
            Roles = roles
        };
    }

    private static StaffRoleChange Command(AccountRecord actor, AccountRecord target, string? role) =>
        new(actor.Id, actor.SecurityStamp, target.Id, target.Version, role, "  Roster change  ", "test-request");
}
