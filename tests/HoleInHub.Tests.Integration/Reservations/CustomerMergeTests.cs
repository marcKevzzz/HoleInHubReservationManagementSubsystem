using HoleInHub.Data.Customers;
using HoleInHub.Data.Migrations.Definitions;
using HoleInHub.Data.Reservations;
using HoleInHub.Tests.Integration.Fixtures;
using MongoDB.Driver;
using Xunit;

namespace HoleInHub.Tests.Integration.Reservations;

[Trait("Category", "Integration")]
public sealed class CustomerMergeTests(MongoDbFixture fixture) : IClassFixture<MongoDbFixture>
{
    private static ReservationRecord Booking(string owner, string name) => new()
    {
        AccountId = owner,
        FirstName = name,
        LastName = "Santos",
        Email = owner.Replace(':', '.') + "@example.test",
        Phone = "09175550123",
        Reference = "HIH-" + Guid.NewGuid().ToString("N")[..10],
        Service = "Regular",
        Status = "Confirmed",
        StartUtc = DateTime.UtcNow.AddDays(1),
        EndUtc = DateTime.UtcNow.AddDays(1).AddHours(1),
        CreatedUtc = DateTime.UtcNow,
        ResourceIds = ["bay-2"],
        IdempotencyKey = Guid.NewGuid().ToString("N"),
        SubmissionHash = "test"
    };
    private static CustomerMergeWrite Command(CustomerProfileSnapshot source, CustomerProfileSnapshot target, string[]? selected = null) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        SourceId = source.Profile.Id,
        TargetId = target.Profile.Id,
        SourceContactRevision = source.Profile.ContactRevision,
        SourceMembershipRevision = source.Profile.MembershipRevision,
        TargetContactRevision = target.Profile.ContactRevision,
        TargetMembershipRevision = target.Profile.MembershipRevision,
        ReservationIds = selected,
        NameFrom = "target",
        PhoneFrom = "source",
        EmailFrom = "target",
        Reason = "Same customer confirmed at the venue",
        ActorId = "staff-1",
        ActorLabel = "Test staff",
        OccurredUtc = DateTime.UtcNow
    };
    [Fact]
    public async Task MergeAndUndoPreserveBookingSnapshotsOwnershipAndIdempotency()
    {
        Assert.True(fixture.IsServerAvailable);
        var context = fixture.CreateDbContext();
        await new Migration_002_Reservations().UpAsync(context.Database, default);
        var first = Booking("account-one", "Alex"); var second = Booking("staff:employee", "Alexander");
        var reservations = context.Database.GetCollection<ReservationRecord>("reservations");
        await reservations.InsertManyAsync([first, second]);
        await new Migration_018_CustomerProfiles().UpAsync(context.Database, default);
        var store = new CustomerProfilePersistence(context);
        var source = (await store.ReadAsync(second.Id, default))!;
        var target = (await store.ReadAsync(first.Id, default))!;
        var command = Command(source, target);
        var operation = await store.MergeAsync(command, default);
        Assert.Equal(operation.Id, (await store.MergeAsync(command, default)).Id);
        Assert.Equal(2, (await store.ReadAsync(first.Id, default))!.ReservationIds.Length);
        var aliases = await store.SearchAsync("Alexander", [], 1, default);
        Assert.Single(aliases.Items);
        Assert.Equal("Alex", aliases.Items[0].Contact.FirstName);
        Assert.Equal(target.Profile.Id, (await store.ReadAsync(second.Id, default))!.Profile.Id);
        Assert.Equal("Alexander", (await reservations.Find(x => x.Id == second.Id).SingleAsync()).FirstName);
        Assert.Equal("staff:employee", (await reservations.Find(x => x.Id == second.Id).SingleAsync()).AccountId);
        Assert.Single(await new ReservationPersistence(context).OwnedAsync("account-one", default));
        await new Migration_018_CustomerProfiles().UpAsync(context.Database, default);
        Assert.Equal(2, (await store.ReadAsync(first.Id, default))!.ReservationIds.Length);
        var undoId = Guid.NewGuid().ToString("N");
        var undo = await store.UndoAsync(operation.Id, undoId, "Wrong records selected", "staff-1", "Test staff", DateTime.UtcNow, default);
        Assert.Equal(undo.Id, (await store.UndoAsync(operation.Id, undoId, "Wrong records selected", "staff-1", "Test staff", undo.OccurredUtc, default)).Id);
        Assert.Single((await store.ReadAsync(first.Id, default))!.ReservationIds);
        Assert.Single((await store.ReadAsync(second.Id, default))!.ReservationIds);
    }
    [Fact]
    public async Task SelectedBookingMergeLeavesSourceAndRejectsStaleMergeAndUndo()
    {
        var context = fixture.CreateDbContext();
        await new Migration_002_Reservations().UpAsync(context.Database, default);
        var a = Booking("same-account", "Alex"); var b = Booking("same-account", "Alexander"); var c = Booking("other-account", "Alex");
        await context.Database.GetCollection<ReservationRecord>("reservations").InsertManyAsync([a, b, c]);
        await new Migration_018_CustomerProfiles().UpAsync(context.Database, default);
        var store = new CustomerProfilePersistence(context);
        var source = (await store.ReadAsync(a.Id, default))!; var target = (await store.ReadAsync(c.Id, default))!;
        var command = Command(source, target, [a.Id]);
        var op = await store.MergeAsync(command, default);
        Assert.Single((await store.ReadAsync(b.Id, default))!.ReservationIds);
        Assert.Equal(2, (await store.ReadAsync(c.Id, default))!.ReservationIds.Length);
        await Assert.ThrowsAsync<CustomerProfileConflictException>(() => store.MergeAsync(Command(source, target, [b.Id]), default));
        var freshSource = (await store.ReadAsync(b.Id, default))!; var freshTarget = (await store.ReadAsync(c.Id, default))!;
        await store.MergeAsync(Command(freshSource, freshTarget), default);
        await Assert.ThrowsAsync<CustomerProfileConflictException>(() => store.UndoAsync(op.Id, Guid.NewGuid().ToString("N"), "Undo", "staff-1", "Test staff", DateTime.UtcNow, default));
    }

    [Fact]
    public async Task OppositeConcurrentMergesHaveOneWinnerAndNoCycle()
    {
        var context = fixture.CreateDbContext();
        await new Migration_002_Reservations().UpAsync(context.Database, default);
        var a = Booking("first-account", "Alex"); var b = Booking("second-account", "Alexander");
        await context.Database.GetCollection<ReservationRecord>("reservations").InsertManyAsync([a, b]);
        await new Migration_018_CustomerProfiles().UpAsync(context.Database, default);
        var store = new CustomerProfilePersistence(context);
        var source = (await store.ReadAsync(a.Id, default))!; var target = (await store.ReadAsync(b.Id, default))!;
        async Task<bool> Apply(CustomerMergeWrite command)
        {
            try { await store.MergeAsync(command, default); return true; }
            catch (CustomerProfileConflictException) { return false; }
        }
        var outcomes = await Task.WhenAll(Apply(Command(source, target)), Apply(Command(target, source)));
        Assert.Single(outcomes, x => x);
        Assert.Equal((await store.ReadAsync(a.Id, default))!.Profile.Id, (await store.ReadAsync(b.Id, default))!.Profile.Id);
    }
    [Fact]
    public async Task NewLinkedStaffBookingPreservesOwnerAndMakesAnOlderUndoStale()
    {
        var context = fixture.CreateDbContext();
        await new Migration_002_Reservations().UpAsync(context.Database, default);
        var a = Booking("account-one", "Alex"); var b = Booking("staff:other", "Alexander");
        await context.Database.GetCollection<ReservationRecord>("reservations").InsertManyAsync([a, b]);
        await new Migration_018_CustomerProfiles().UpAsync(context.Database, default);
        var store = new CustomerProfilePersistence(context);
        var op = await store.MergeAsync(Command((await store.ReadAsync(b.Id, default))!, (await store.ReadAsync(a.Id, default))!), default);
        var service = new HoleInHub.Business.Reservations.ReservationService(new ReservationPersistence(context), new OutboxPersistence(context),
            Microsoft.Extensions.Options.Options.Create(new HoleInHub.Business.Reservations.ReservationPolicy()));
        var request = new HoleInHub.Business.Reservations.StaffBookingRequest("bay-2", DateOnly.FromDateTime(DateTime.UtcNow.AddDays(3)), new TimeOnly(13, 0), 30, "Alex", "Santos", "09175550123", 2, "Walk-in", Guid.NewGuid().ToString("N"))
        { CustomerProfileId = op.TargetId };
        var saved = await service.CreateStaffBookingAsync(request, "employee", default);
        Assert.Equal(saved.Id, (await service.CreateStaffBookingAsync(request, "employee", default)).Id);
        Assert.Equal(3, (await store.ReadAsync(a.Id, default))!.ReservationIds.Length);
        var record = await context.Database.GetCollection<ReservationRecord>("reservations").Find(x => x.Id == saved.Id).SingleAsync();
        Assert.Equal("staff:employee", record.AccountId); Assert.Null(record.CustomerAccountId);
        await Assert.ThrowsAsync<CustomerProfileConflictException>(() => store.UndoAsync(op.Id, Guid.NewGuid().ToString("N"), "Undo", "staff-1", "Staff", DateTime.UtcNow, default));
    }

    [Fact]
    public async Task NewVerifiedGuestsReuseEmailProfileAndPreserveBookingContactsAndOwnership()
    {
        var context = fixture.CreateDbContext();
        await new Migration_002_Reservations().UpAsync(context.Database, default);
        var first = Booking("guest:old-a", "Alex"); first.EmailVerificationId = "old-a";
        var second = Booking("guest:old-b", "Family"); second.EmailVerificationId = "old-b";
        second.Email = first.Email;
        var records = context.Database.GetCollection<ReservationRecord>("reservations");
        await records.InsertManyAsync([first, second]);
        await new Migration_018_CustomerProfiles().UpAsync(context.Database, default);
        var store = new CustomerProfilePersistence(context);
        Assert.Equal((await store.ReadAsync(first.Id, default))!.Profile.Id, (await store.ReadAsync(second.Id, default))!.Profile.Id);
        var originalName = (await store.ReadAsync(first.Id, default))!.Profile.FirstName;
        var a = Booking("guest:new-a", "Alex"); a.EmailVerificationId = "new-a";
        var b = Booking("guest:new-b", "Different"); b.EmailVerificationId = "new-b";
        a.Email = first.Email;
        b.Email = "  " + a.Email.ToUpperInvariant() + "  ";
        b.Phone = "01012345678";
        foreach (var record in new[] { a, b })
        {
            using var session = await context.Client.StartSessionAsync();
            await session.WithTransactionAsync(async (transaction, token) =>
            {
                await records.InsertOneAsync(transaction, record, cancellationToken: token);
                await store.AttachAsync(transaction, record, null, token);
                return true;
            });
        }
        var shared = (await store.ReadAsync(a.Id, default))!;
        Assert.Equal(originalName, shared.Profile.FirstName);
        Assert.Equal(shared.Profile.Id, (await store.ReadAsync(b.Id, default))!.Profile.Id);
        Assert.Equal((await store.ReadAsync(first.Id, default))!.Profile.Id, shared.Profile.Id);
        Assert.Contains(shared.Profile.Variants, contact => contact.FirstName == "Different" && contact.Phone == b.Phone);
        Assert.Equal("Different", (await records.Find(x => x.Id == b.Id).SingleAsync()).FirstName);
        Assert.Equal("guest:new-b", (await records.Find(x => x.Id == b.Id).SingleAsync()).AccountId);
        await new Migration_018_CustomerProfiles().UpAsync(context.Database, default);
        Assert.Equal(4, (await store.ReadAsync(a.Id, default))!.ReservationIds.Length);
    }
    [Fact]
    public async Task GuestAndAccountBookingsReuseManualEmailContactWithoutChangingItsPrimaryName()
    {
        var context = fixture.CreateDbContext();
        await new Migration_002_Reservations().UpAsync(context.Database, default);
        await new Migration_018_CustomerProfiles().UpAsync(context.Database, default);
        var store = new CustomerProfilePersistence(context);
        var original = new CustomerProfileRecord
        {
            Id = "cp_" + new string('a', 64),
            Kind = "Manual",
            FirstName = "Original",
            LastName = "Santos",
            Email = "shared@example.test",
            Phone = "09175550123",
            CreatedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow
        };
        Assert.Equal(original.Id, await store.CreateContactAsync(original, default));
        var alternate = new CustomerProfileRecord
        {
            Id = "cp_" + new string('b', 64),
            Kind = "Manual",
            FirstName = "Alternate",
            LastName = "Santos",
            Email = "SHARED@example.test",
            Phone = "01012345678",
            CreatedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow
        };
        Assert.Equal(original.Id, await store.CreateContactAsync(alternate, default));
        Assert.Equal(original.Id, await store.CreateContactAsync(alternate, default));
        var records = context.Database.GetCollection<ReservationRecord>("reservations");
        foreach (var owner in new[] { "guest:new", "account-new" })
        {
            var booking = Booking(owner, "Booking name");
            booking.Email = "shared@example.test";
            booking.EmailVerificationId = "verified";
            using var session = await context.Client.StartSessionAsync();
            await session.WithTransactionAsync(async (transaction, token) =>
            {
                await records.InsertOneAsync(transaction, booking, cancellationToken: token);
                await store.AttachAsync(transaction, booking, null, token);
                return true;
            });
            Assert.Equal(original.Id, (await store.ReadAsync(booking.Id, default))!.Profile.Id);
        }
        var snapshot = (await store.ReadAsync(original.Id, default))!;
        Assert.Equal("Original", snapshot.Profile.FirstName);
        Assert.Equal(2, snapshot.ReservationIds.Length);
        Assert.Contains(snapshot.Profile.Variants, variant => variant.Phone == alternate.Phone);
        Assert.Single((await store.SearchAsync("", [], 1, default)).Items);
        alternate.Phone = "09175550999";
        await Assert.ThrowsAsync<CustomerProfileConflictException>(() => store.CreateContactAsync(alternate, default));
    }

    [Fact]
    public async Task ReversingDependentMergeMakesTheOriginalMergeUndoableAgain()
    {
        var context = fixture.CreateDbContext();
        await new Migration_002_Reservations().UpAsync(context.Database, default);
        var a = Booking("account-a", "Alex"); var b = Booking("account-b", "Alexander"); var c = Booking("account-c", "Alexandra");
        await context.Database.GetCollection<ReservationRecord>("reservations").InsertManyAsync([a, b, c]);
        await new Migration_018_CustomerProfiles().UpAsync(context.Database, default);
        var store = new CustomerProfilePersistence(context);
        var first = await store.MergeAsync(Command((await store.ReadAsync(a.Id, default))!, (await store.ReadAsync(b.Id, default))!), default);
        var second = await store.MergeAsync(Command((await store.ReadAsync(b.Id, default))!, (await store.ReadAsync(c.Id, default))!), default);
        Assert.False(await store.CanUndoAsync(first, default));
        Assert.Contains((await store.HistoryAsync((await store.ReadAsync(c.Id, default))!.Profile.Id, 1, default)), entry => entry.Id == first.Id);
        await store.UndoAsync(second.Id, Guid.NewGuid().ToString("N"), "Reverse latest", "staff-1", "Staff", DateTime.UtcNow, default);
        Assert.True(await store.CanUndoAsync(first, default));
        await store.UndoAsync(first.Id, Guid.NewGuid().ToString("N"), "Reverse original", "staff-1", "Staff", DateTime.UtcNow, default);
        Assert.Single((await store.ReadAsync(a.Id, default))!.ReservationIds);
        Assert.Single((await store.ReadAsync(b.Id, default))!.ReservationIds);
        Assert.Single((await store.ReadAsync(c.Id, default))!.ReservationIds);
    }
}
