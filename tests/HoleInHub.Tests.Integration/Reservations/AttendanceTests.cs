using HoleInHub.Business.Reservations;
using HoleInHub.Data.Migrations.Definitions;
using HoleInHub.Data.Reservations;
using HoleInHub.Tests.Integration.Fixtures;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Xunit;

namespace HoleInHub.Tests.Integration.Reservations;

[Trait("Category", "Integration")]
public sealed class AttendanceTests(MongoDbFixture fixture) : IClassFixture<MongoDbFixture>
{
    private static readonly DateTime Start = new(2026, 11, 10, 5, 0, 0, DateTimeKind.Utc);
    private sealed class Clock : TimeProvider
    {
        public DateTime Now = Start;
        public override DateTimeOffset GetUtcNow() => new(Now);
    }
    private async Task<(ReservationPersistence Store, HoleInHub.Data.Context.IMongoDbContext Context)> Setup(bool attendance = true)
    {
        Assert.True(fixture.IsServerAvailable, "Start the local MongoDB replica set.");
        var context = fixture.CreateDbContext();
        await new Migration_002_Reservations().UpAsync(context.Database, default);
        await new AccountsMigration().UpAsync(context.Database, default);
        await new Migration_004_BookingProcess().UpAsync(context.Database, default);
        await new Migration_005_GoogleAccounts().UpAsync(context.Database, default);
        await new Migration_006_SchemaHardening().UpAsync(context.Database, default);
        await new Migration_010_StaffAssistedReservations().UpAsync(context.Database, default);
        if (attendance) { await new Migration_013_ReservationAttendance().UpAsync(context.Database, default); }
        return (new(context), context);
    }
    private static ReservationRecord Booking(string status = "Confirmed", string service = "Regular", string[]? resources = null) => new()
    {
        AccountId = "customer",
        Email = "customer@example.test",
        Reference = Guid.NewGuid().ToString("N"),
        FirstName = "Alex",
        LastName = "Santos",
        Phone = "09175550123",
        Attendees = 1,
        Service = service,
        Status = status,
        PolicyVersion = 2,
        ResourceIds = resources ?? ["bay-2"],
        StartUtc = Start,
        EndUtc = Start.AddHours(2),
        CreatedUtc = Start.AddDays(-1),
        ReviewDeadlineUtc = Start.AddHours(1),
        IdempotencyKey = Guid.NewGuid().ToString("N"),
        SubmissionHash = "test"
    };
    private static AttendanceCommand Command(string id, string action = "check-in", long revision = 0, string? reason = null) =>
        new(id, action, revision, Guid.NewGuid().ToString("N"), reason);
    private static ReservationService Service(ReservationPersistence store, HoleInHub.Data.Context.IMongoDbContext context, Clock clock) =>
        new(store, new OutboxPersistence(context), Options.Create(new ReservationPolicy()), clock);

    [Fact]
    public async Task ArrivalDepartureAndOldRetriesPreserveAuditAndReleaseCapacity()
    {
        var (store, context) = await Setup(); var clock = new Clock(); var service = Service(store, context, clock);
        var booking = await store.InsertAsync(Booking(), default);
        var arrival = Command(booking.Id);
        Assert.Equal(AttendanceResult.Applied, await service.RecordAttendanceAsync(arrival, "front-desk", default));
        Assert.Equal(AttendanceResult.Stale, await service.RecordAttendanceAsync(Command(booking.Id, "check-out", 0, "Finished"), "front-desk", default));
        clock.Now = Start.AddMinutes(30);
        var departure = Command(booking.Id, "check-out", 1, "  Finished early  ");
        Assert.Equal(AttendanceResult.Applied, await service.RecordAttendanceAsync(departure, "front-desk", default));
        Assert.Equal(AttendanceResult.AlreadyApplied, await service.RecordAttendanceAsync(departure, "front-desk", default));
        Assert.Equal(AttendanceResult.AlreadyApplied, await service.RecordAttendanceAsync(arrival, "front-desk", default));
        await Assert.ThrowsAsync<ReservationValidationException>(() => service.RecordAttendanceAsync(arrival, "different-user", default));
        var saved = (await store.ByIdAsync(booking.Id, default))!;
        Assert.Equal("Completed", saved.Status); Assert.Equal(2, saved.Revision);
        Assert.Equal(Start, saved.StartUtc); Assert.Equal(Start.AddHours(2), saved.EndUtc);
        Assert.Equal(Start, saved.CheckedInUtc); Assert.Equal(clock.Now, saved.CheckedOutUtc);
        Assert.Equal(2, saved.History.Count);
        Assert.Equal("Confirmed", saved.History[0].PreviousReservationStatus);
        Assert.Equal("Checked-In", saved.History[1].PreviousReservationStatus);
        Assert.Equal("Finished early", saved.History[1].Reason);
        Assert.Equal("front-desk", saved.History[1].Actor);
        Assert.DoesNotContain("bay-2", await store.OccupiedAsync(clock.Now, clock.Now.AddMinutes(30), default));
        var next = Booking(); next.StartUtc = clock.Now; next.EndUtc = Start.AddHours(1);
        await store.InsertAsync(next, default);
        Assert.Contains((await service.GetStaffScheduleAsync(new DateOnly(2026, 11, 10), default)).SelectMany(x => x.Entries), x => x.ReservationId == booking.Id && x.Status == "Completed");
        Assert.Equal(2, await context.Database.GetCollection<OutboxRecord>("outbox").CountDocumentsAsync(FilterDefinition<OutboxRecord>.Empty));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ConcurrentArrivalsWriteOneEvent(bool sameCommand)
    {
        var (store, context) = await Setup(); var service = Service(store, context, new Clock());
        var booking = await store.InsertAsync(Booking(), default); var command = Command(booking.Id);
        var results = await Task.WhenAll(service.RecordAttendanceAsync(command, "staff", default),
            service.RecordAttendanceAsync(sameCommand ? command : Command(booking.Id), "staff", default));
        Assert.Single(results, x => x == AttendanceResult.Applied);
        Assert.Contains(sameCommand ? AttendanceResult.AlreadyApplied : AttendanceResult.Stale, results);
        Assert.Single((await store.ByIdAsync(booking.Id, default))!.History);
    }

    [Fact]
    public async Task ArrivalRechecksBlockInactiveResourceAndPendingCancellation()
    {
        var (store, context) = await Setup(); var service = Service(store, context, new Clock());
        var booking = await store.InsertAsync(Booking(), default);
        var blocks = context.Database.GetCollection<ScheduleBlock>("schedule_blocks");
        await blocks.InsertOneAsync(new() { Id = "legacy-closure", StartUtc = Start, EndUtc = Start.AddHours(2) });
        Assert.Equal(AttendanceResult.NotEligible, await service.RecordAttendanceAsync(Command(booking.Id), "staff", default));
        await blocks.DeleteOneAsync(x => x.Id == "legacy-closure");
        await context.Database.GetCollection<ResourceRecord>("resources").UpdateOneAsync(x => x.Id == "bay-2", Builders<ResourceRecord>.Update.Set(x => x.Active, false));
        Assert.Equal(AttendanceResult.NotEligible, await service.RecordAttendanceAsync(Command(booking.Id), "staff", default));
        Assert.Empty((await store.ByIdAsync(booking.Id, default))!.History);
        Assert.Equal(AttendanceResult.NotFound, await service.RecordAttendanceAsync(Command("missing"), "staff", default));
    }

    [Theory]
    [InlineData("Regular")]
    [InlineData("WholeShop")]
    public async Task AllAllocatedBaysReleaseTogether(string type)
    {
        var (store, context) = await Setup(); var clock = new Clock(); var service = Service(store, context, clock);
        var record = Booking(service: type, resources: ["bay-2", "bay-3"]); record.PaymentStatus = type == "WholeShop" ? "DepositReceived" : "NotRequired";
        if (type == "WholeShop") { record.ResourceIds = (await store.CatalogAsync(default)).Select(x => x.Id).ToArray(); }
        var booking = await store.InsertAsync(record, default);
        await service.RecordAttendanceAsync(Command(booking.Id), "staff", default);
        clock.Now = Start.AddMinutes(30);
        Assert.Equal(AttendanceResult.Applied, await service.RecordAttendanceAsync(Command(booking.Id, "check-out", 1, "All bays clear"), "staff", default));
        Assert.Empty(await store.OccupiedAsync(clock.Now, Start.AddHours(1), default));
        Assert.Equal(record.ResourceIds.Order(), (await store.ByIdAsync(booking.Id, default))!.ResourceIds.Order());
    }

    [Fact]
    public async Task LegacyArrivalCanDepartWithoutInventingArrivalTimeAndMigrationReruns()
    {
        var (store, context) = await Setup(false);
        var document = Booking("Checked-In").ToBsonDocument(); document.Remove("Revision");
        await context.Database.GetCollection<BsonDocument>("reservations").InsertOneAsync(document);
        var migration = new Migration_013_ReservationAttendance();
        await migration.UpAsync(context.Database, default); await migration.UpAsync(context.Database, default);
        var clock = new Clock { Now = Start.AddHours(3) };
        Assert.Equal(AttendanceResult.Applied, await Service(store, context, clock).RecordAttendanceAsync(Command(document["_id"].AsString, "check-out"), "staff", default));
        var saved = (await store.ByIdAsync(document["_id"].AsString, default))!;
        Assert.Null(saved.CheckedInUtc); Assert.Equal(clock.Now, saved.CheckedOutUtc); Assert.Equal(1, saved.Revision);
        await Assert.ThrowsAsync<MongoWriteException>(() => context.Database.GetCollection<BsonDocument>("reservations").UpdateOneAsync(new BsonDocument("_id", saved.Id), new BsonDocument("$set", new BsonDocument("Revision", -1))));
    }
    [Fact]
    public async Task ExistingWritersIncrementRevisionAndStaleReviewCannotApprove()
    {
        var (store, context) = await Setup();
        var coaching = Booking("Pending", "PhilippineCoach");
        coaching.StartUtc = Start.AddMinutes(1);
        var saved = await store.InsertAsync(coaching, default);
        Assert.True(await store.TransitionAsync(saved.Id, "coach", "staff", Start, [], default, expectedRevision: 0));
        Assert.False(await store.TransitionAsync(saved.Id, "approve", "staff", Start, [], default, expectedRevision: 0));
        Assert.True(await store.TransitionAsync(saved.Id, "approve", "staff", Start, [], default, expectedRevision: 1));
        Assert.Equal(2, (await store.ByIdAsync(saved.Id, default))!.Revision);
        var expired = Booking("Pending", resources: ["bay-3"]); expired.ReviewDeadlineUtc = Start.AddMinutes(-1);
        await store.InsertAsync(expired, default); await store.ExpireAsync(Start, [], default);
        Assert.Equal(1, (await store.ByIdAsync(expired.Id, default))!.Revision);
        var paid = Booking("Pending", "WholeShop"); paid.PolicyVersion = 1;
        paid.StartUtc = Start.AddDays(1); paid.EndUtc = paid.StartUtc.AddHours(2);
        paid.Participants = (await store.CatalogAsync(default)).Select(x => new ParticipantRecord { ResourceId = x.Id, Players = 1 }).ToArray();
        paid.PaymentStatus = "AwaitingDeposit";
        await store.InsertAsync(paid, default);
        Assert.True(await store.ApplyDemoPaymentAsync(paid.Id, "success", "demo", Start, default));
        Assert.True(await store.ApplyDemoPaymentAsync(paid.Id, "success", "demo", Start, default));
        Assert.Equal(1, (await store.ByIdAsync(paid.Id, default))!.Revision);
    }

    [Fact]
    public async Task CancellationAndArrivalCannotBothCommit()
    {
        var (store, context) = await Setup(); var service = Service(store, context, new Clock());
        var record = Booking(); record.CancellationRequested = true;
        await store.InsertAsync(record, default);
        var arrival = service.RecordAttendanceAsync(Command(record.Id), "staff", default);
        Assert.True(await store.TransitionAsync(record.Id, "cancel", "staff", Start, [], default, expectedRevision: 0));
        Assert.Contains(await arrival, new[] { AttendanceResult.NotEligible, AttendanceResult.Stale });
        var saved = (await store.ByIdAsync(record.Id, default))!;
        Assert.Equal("Cancelled", saved.Status); Assert.Null(saved.CheckedInUtc); Assert.Single(saved.History);
    }

    [Fact]
    public async Task CheckoutRacingNewAllocationNeverLeavesAnActiveOverlap()
    {
        var (store, context) = await Setup(); var clock = new Clock { Now = Start.AddMinutes(30) };
        var service = Service(store, context, clock);
        var original = await store.InsertAsync(Booking("Checked-In"), default);
        var next = Booking(); next.StartUtc = clock.Now; next.EndUtc = Start.AddHours(1);
        async Task<bool> Allocate() { try { await store.InsertAsync(next, default); return true; } catch (AllocationConflictException) { return false; } }
        var allocation = Allocate();
        Assert.Equal(AttendanceResult.Applied, await service.RecordAttendanceAsync(Command(original.Id, "check-out", 0, "Bay clear"), "staff", default));
        if (!await allocation) { await store.InsertAsync(next, default); }
        Assert.Equal("Completed", (await store.ByIdAsync(original.Id, default))!.Status);
        Assert.Equal("Confirmed", (await store.ByIdAsync(next.Id, default))!.Status);
    }

    [Theory]
    [InlineData("Revision")]
    [InlineData("CheckedInUtc")]
    [InlineData("CheckedOutUtc")]
    public async Task MigrationPreflightRejectsBadLegacyFieldsWithoutBackfilling(string field)
    {
        var (_, context) = await Setup(false);
        var document = Booking().ToBsonDocument(); document.Remove("Revision");
        document[field] = field == "Revision" ? new BsonInt64(-1) : new BsonString("not-a-date");
        var records = context.Database.GetCollection<BsonDocument>("reservations");
        await records.InsertOneAsync(document);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new Migration_013_ReservationAttendance().UpAsync(context.Database, default));
        var unchanged = await records.Find(new BsonDocument("_id", document["_id"])).SingleAsync();
        Assert.Equal(document[field], unchanged[field]);
        if (field != "Revision") { Assert.False(unchanged.Contains("Revision")); }
    }

    [Fact]
    public async Task FollowUpFlagNeverAutomaticallyChangesAttendance()
    {
        var (store, context) = await Setup(); var clock = new Clock { Now = Start.AddHours(1).AddTicks(-1) };
        var service = Service(store, context, clock); var booking = await store.InsertAsync(Booking(), default);
        Assert.False((await service.GetStaffReviewAsync(booking.Id, default))!.NeedsFollowUp);
        clock.Now = Start.AddHours(1);
        var review = (await service.GetStaffReviewAsync(booking.Id, default))!;
        Assert.True(review.NeedsFollowUp); Assert.True(review.CanCheckIn); Assert.Equal("Confirmed", review.Booking.Status);
        Assert.Empty((await store.ByIdAsync(booking.Id, default))!.History);
    }

}
