using HoleInHub.Business.Accounts;
using HoleInHub.Business.Customers;
using HoleInHub.Business.Reservations;
using HoleInHub.Data.Accounts;
using HoleInHub.Data.Customers;
using HoleInHub.Data.Migrations.Definitions;
using HoleInHub.Data.Reservations;
using HoleInHub.Tests.Integration.Fixtures;
using MongoDB.Bson;
using MongoDB.Driver;
using Xunit;

namespace HoleInHub.Tests.Integration.Accounts;

[Trait("Category", "Integration")]
public sealed class TeamCreationTests(MongoDbFixture fixture) : IClassFixture<MongoDbFixture>
{
    [Theory]
    [InlineData("Manager", "Staff", TeamCreationResult.Created)]
    [InlineData("Manager", "Manager", TeamCreationResult.Created)]
    [InlineData("Manager", "Admin", TeamCreationResult.Forbidden)]
    [InlineData("Staff", "Staff", TeamCreationResult.Forbidden)]
    [InlineData("Admin", "Admin", TeamCreationResult.Created)]
    public async Task CreationRechecksPersistedRolesAndCommitsInvitationAndAuditTogether(string actorRole, string requestedRole, TeamCreationResult expected)
    {
        var db = fixture.CreateDbContext();
        await new Migration_017_StaffRoleAdministration().UpAsync(db.Database, default);
        var persistence = new AccountPersistence(db);
        var actor = new AccountRecord { Roles = [actorRole], EmailConfirmed = true, Email = "actor@example.test", NormalizedEmail = "ACTOR@EXAMPLE.TEST" };
        await persistence.CreateAsync(actor, default);
        var service = new AccountService(persistence, TimeProvider.System);
        var target = new AccountDetails(Guid.NewGuid().ToString("N"), "new@example.test", "NEW@EXAMPLE.TEST", "random-hash", "stamp", "version", false, 0, null, ["Customer"])
        { FirstName = "New", LastName = "Member", Phone = "09175550123" };
        var result = await service.CreateTeamMemberAsync(target, requestedRole, actor.Id, actor.SecurityStamp, "protected:test-invitation", default);
        Assert.Equal(expected, result);
        var messages = db.Database.GetCollection<OutboxRecord>("outbox");
        var audit = db.Database.GetCollection<BsonDocument>("staff_role_audit");
        Assert.Equal(expected == TeamCreationResult.Created ? 1 : 0, await messages.CountDocumentsAsync(FilterDefinition<OutboxRecord>.Empty));
        Assert.Equal(expected == TeamCreationResult.Created ? 1 : 0, await audit.CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty));
        if (expected != TeamCreationResult.Created) { Assert.Null(await service.FindByIdAsync(target.Id, default)); return; }
        var saved = (await service.FindByIdAsync(target.Id, default))!;
        Assert.False(saved.EmailConfirmed);
        Assert.Equal(new[] { requestedRole }, saved.Roles);
        Assert.Equal(TeamCreationResult.Created, await service.CreateTeamMemberAsync(target, requestedRole, actor.Id, actor.SecurityStamp, "protected:retry", default));
        Assert.Equal(1, await messages.CountDocumentsAsync(FilterDefinition<OutboxRecord>.Empty));
        Assert.Equal(TeamCreationResult.Forbidden, await service.CreateTeamMemberAsync(target with { Id = Guid.NewGuid().ToString("N") }, requestedRole,
            actor.Id, "stale-stamp", "protected:forged", default));
        var resendId = Guid.NewGuid().ToString("N");
        Assert.Equal(TeamCreationResult.Created, await service.CreateTeamMemberAsync(saved, requestedRole, actor.Id, actor.SecurityStamp, "protected:resend", default, resendId));
        Assert.Equal(TeamCreationResult.Created, await service.CreateTeamMemberAsync(saved, requestedRole, actor.Id, actor.SecurityStamp, "protected:resend", default, resendId));
        Assert.Equal(2, await messages.CountDocumentsAsync(FilterDefinition<OutboxRecord>.Empty));
    }

    [Fact]
    public async Task PinResetRequiresAnAdministratorAndInvalidatesCredentialsOnce()
    {
        var db = fixture.CreateDbContext();
        await new Migration_017_StaffRoleAdministration().UpAsync(db.Database, default);
        var persistence = new AccountPersistence(db);
        var admin = new AccountRecord { Roles = ["Admin"], EmailConfirmed = true, Email = "admin@example.test", NormalizedEmail = "ADMIN@EXAMPLE.TEST" };
        var manager = new AccountRecord { Roles = ["Manager"], EmailConfirmed = true, Email = "manager@example.test", NormalizedEmail = "MANAGER@EXAMPLE.TEST" };
        var staff = new AccountRecord { Roles = ["Staff"], EmailConfirmed = true, Email = "staff@example.test", NormalizedEmail = "STAFF@EXAMPLE.TEST" };
        foreach (var account in new[] { admin, manager, staff }) { await persistence.CreateAsync(account, default); }
        var pins = new StaffPinPersistence(db);
        Assert.True(await pins.TrySaveAsync(new(staff.Id, "old-pin-hash", "pin-version"), staff.SecurityStamp, ["Staff"], default));
        var service = new AccountService(persistence, TimeProvider.System);
        var before = (await service.FindByIdAsync(staff.Id, default))!;
        var command = new StaffPinReset(admin.Id, admin.SecurityStamp, staff.Id, before.Version, Guid.NewGuid().ToString("N"), "Forgotten PIN");
        Assert.Equal(StaffRoleChangeResult.Forbidden, await service.ResetStaffPinAsync(command with { ActorId = manager.Id, ActorStamp = manager.SecurityStamp }, default));
        Assert.Equal(StaffRoleChangeResult.Forbidden, await service.ResetStaffPinAsync(command with { TargetId = admin.Id }, default));
        Assert.Equal(StaffRoleChangeResult.Invalid, await service.ResetStaffPinAsync(command with { Reason = " " }, default));
        Assert.NotNull(await pins.ReadAsync(staff.Id, default));
        Assert.Equal(StaffRoleChangeResult.Applied, await service.ResetStaffPinAsync(command, default));
        var after = (await service.FindByIdAsync(staff.Id, default))!;
        Assert.Null(await pins.ReadAsync(staff.Id, default));
        Assert.NotEqual(before.SecurityStamp, after.SecurityStamp);
        Assert.Equal(before.Roles, after.Roles);
        Assert.Equal(StaffRoleChangeResult.Applied, await service.ResetStaffPinAsync(command, default));
        Assert.Equal(after.SecurityStamp, (await service.FindByIdAsync(staff.Id, default))!.SecurityStamp);
        Assert.Equal(StaffRoleChangeResult.Stale, await service.ResetStaffPinAsync(command with { CommandId = Guid.NewGuid().ToString("N") }, default));
        Assert.Equal(1, await db.Database.GetCollection<BsonDocument>("staff_role_audit").CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty));
    }

    [Fact]
    public async Task ManualContactCanBeFoundWithoutInventingAReservationAndRetainsTheFirstBooking()
    {
        var db = fixture.CreateDbContext();
        await new Migration_018_CustomerProfiles().UpAsync(db.Database, default);
        var persistence = new CustomerProfilePersistence(db);
        var service = new CustomerProfileService(persistence, TimeProvider.System);
        var key = Guid.NewGuid().ToString("N");
        var id = await service.CreateAsync(key, "Manual", "Customer", "", "09175550123", "staff", default);
        Assert.Equal(id, await service.CreateAsync(key, "Manual", "Customer", "", "09175550123", "staff", default));
        var directory = await persistence.SearchAsync("Manual", [], 1, default);
        Assert.Equal(id, Assert.Single(directory.Items).Contact.Id);
        var history = await persistence.ReadHistoryAsync(id, false, 1, DateTime.UtcNow, default);
        Assert.NotNull(history);
        Assert.Equal(0, history.Total);
        Assert.Empty(history.Bookings);
        var booking = new ReservationRecord { FirstName = "Manual", LastName = "Customer", Phone = "09175550123", CreatedUtc = DateTime.UtcNow, Reference = "HIH-MANUAL" };
        await db.Database.GetCollection<ReservationRecord>("reservations").InsertOneAsync(booking);
        using var session = await db.Client.StartSessionAsync();
        await session.WithTransactionAsync(async (transaction, ct) => { await persistence.AttachAsync(transaction, booking, id, ct); return true; });
        var updated = await persistence.ReadHistoryAsync(id, false, 1, DateTime.UtcNow, default);
        Assert.NotNull(updated);
        Assert.Equal(1, updated.Total);
        Assert.Equal(booking.Id, Assert.Single(updated.Bookings).Id);
        await Assert.ThrowsAsync<ReservationValidationException>(() => service.CreateAsync(key, "Other", "Contact", "", "09175550123", "staff", default));
    }
}
