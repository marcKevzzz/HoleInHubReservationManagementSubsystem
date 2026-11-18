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
public sealed class ScheduleBlockTests(MongoDbFixture fixture) : IClassFixture<MongoDbFixture>
{
    private async Task<(ReservationPersistence Store, HoleInHub.Data.Context.IMongoDbContext Context)> Setup()
    {
        Assert.True(fixture.IsServerAvailable, "Start the MongoDB replica set.");
        var context = fixture.CreateDbContext();
        await new Migration_002_Reservations().UpAsync(context.Database, default);
        return (new ReservationPersistence(context), context);
    }
    private static readonly DateOnly Day = new(2026, 11, 10);
    private static readonly DateTime Start = new(2026, 11, 10, 5, 0, 0, DateTimeKind.Utc);
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 11, 10, 4, 15, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private static StaffBlockRequest Request(string key = "block") => new("bay-1", Day, new TimeOnly(13, 0), 60, "  Maintenance  ", key);
    private static ReservationRecord Booking(string status, string service = "Regular") => new()
    {
        AccountId = "customer",
        Email = "customer@example.test",
        Status = status,
        Service = service,
        IdempotencyKey = Guid.NewGuid().ToString("N"),
        SubmissionHash = "test",
        ResourceIds = ["bay-1"],
        StartUtc = Start,
        EndUtc = Start.AddHours(1),
        CreatedUtc = Start.AddDays(-1),
        ReviewDeadlineUtc = Start.AddDays(1)
    };
    private static ReservationService Service(ReservationPersistence store, HoleInHub.Data.Context.IMongoDbContext context, Clock? clock = null) =>
        new(store, new OutboxPersistence(context), Options.Create(new ReservationPolicy()), clock ?? new Clock());

    [Fact]
    public async Task WholeDayVenueBlockCoversEveryBayAndReleasesAsOneAuditedBlock()
    {
        var (store, context) = await Setup();
        var service = Service(store, context);
        var request = Request("venue-day") with { ResourceId = "*", WholeDay = true };
        var id = await service.CreateScheduleBlockAsync(request, "admin", default);
        var block = (await store.BlockByIdAsync(id, default))!;
        Assert.Empty(block.ResourceIds);
        Assert.Equal(new DateTime(2026, 11, 10, 2, 0, 0, DateTimeKind.Utc), block.StartUtc);
        Assert.Equal(new DateTime(2026, 11, 10, 16, 0, 0, DateTimeKind.Utc), block.EndUtc);
        Assert.Equal(id, await service.CreateScheduleBlockAsync(request, "admin", default));
        await Assert.ThrowsAsync<AllocationConflictException>(() => store.InsertAsync(Booking("Confirmed"), default));
        var vip = Booking("Confirmed", "VIP");
        vip.ResourceIds = ["vip-7"];
        await Assert.ThrowsAsync<AllocationConflictException>(() => store.InsertAsync(vip, default));
        Assert.True(await service.ReleaseScheduleBlockAsync(id, "Venue reopened", "admin", default));
        await store.InsertAsync(Booking("Confirmed"), default);
        Assert.NotNull((await store.BlockByIdAsync(id, default))!.ReleasedUtc);
    }
    [Fact]
    public async Task SavesAuditAndUtcReplaysAfterTimePassesAndRejectsChangedPayload()
    {
        var (store, context) = await Setup();
        var clock = new Clock();
        var service = Service(store, context, clock);
        var id = await service.CreateScheduleBlockAsync(Request(), "admin", default);
        var saved = (await store.BlockByIdAsync(id, default))!;
        Assert.Equal(Start, saved.StartUtc);
        Assert.Equal(Start.AddHours(1), saved.EndUtc);
        Assert.Equal("Maintenance", saved.Reason);
        Assert.Equal("admin", saved.CreatedBy);
        Assert.Equal(clock.Now.UtcDateTime, saved.CreatedUtc);
        Assert.Equal("Maintenance", Assert.Single((await service.GetStaffScheduleAsync(Day, default))[0].Entries).Reason);
        clock.Now = clock.Now.AddDays(1);
        Assert.Equal(id, await service.CreateScheduleBlockAsync(Request(), "admin", default));
        await Assert.ThrowsAsync<ReservationValidationException>(() => service.CreateScheduleBlockAsync(Request() with { Reason = "Other" }, "admin", default));
        Assert.Equal(1, await context.Database.GetCollection<ScheduleBlock>("schedule_blocks").CountDocumentsAsync(FilterDefinition<ScheduleBlock>.Empty));
        Assert.Equal(0, await context.Database.GetCollection<OutboxRecord>("outbox").CountDocumentsAsync(FilterDefinition<OutboxRecord>.Empty));
    }

    [Theory]
    [InlineData("Pending", "Regular")]
    [InlineData("Confirmed", "Regular")]
    [InlineData("Checked-In", "VIP")]
    [InlineData("Confirmed", "WholeShop")]
    public async Task RejectsEveryLiveBookingState(string status, string service)
    {
        var (store, context) = await Setup();
        var booking = Booking(status, service);
        var resource = service == "VIP" ? "vip-7" : "bay-1";
        booking.ResourceIds = [resource];
        await store.InsertAsync(booking, default);
        await Assert.ThrowsAsync<ReservationValidationException>(() => Service(store, context).CreateScheduleBlockAsync(Request() with { ResourceId = resource }, "admin", default));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RejectsTargetedAndVenueWideClosures(bool targeted)
    {
        var (store, context) = await Setup();
        await context.Database.GetCollection<ScheduleBlock>("schedule_blocks").InsertOneAsync(new()
        { Id = "closure", ResourceIds = targeted ? ["bay-1"] : [], StartUtc = Start, EndUtc = Start.AddHours(1) });
        await Assert.ThrowsAsync<ReservationValidationException>(() => Service(store, context).CreateScheduleBlockAsync(Request(), "admin", default));
    }

    [Fact]
    public async Task AdjacentBlocksAndOtherBaysAreAllowedButOverlapIsRejected()
    {
        var (store, context) = await Setup();
        var service = Service(store, context);
        await service.CreateScheduleBlockAsync(Request(), "admin", default);
        await service.CreateScheduleBlockAsync(Request("adjacent") with { StartTime = new TimeOnly(14, 0) }, "admin", default);
        await service.CreateScheduleBlockAsync(Request("other-bay") with { ResourceId = "bay-2" }, "admin", default);
        await Assert.ThrowsAsync<ReservationValidationException>(() => service.CreateScheduleBlockAsync(Request("overlap"), "other-admin", default));
        await Assert.ThrowsAsync<AllocationConflictException>(() => store.InsertAsync(Booking("Pending"), default));
        var booking = Booking("Confirmed");
        booking.StartUtc = Start.AddHours(2); booking.EndUtc = Start.AddHours(3);
        await store.InsertAsync(booking, default);
    }

    [Fact]
    public async Task PastAndInactiveBaysAreRejectedAndCurrentHalfHourIsAllowed()
    {
        var (store, context) = await Setup();
        var service = Service(store, context);
        await Assert.ThrowsAsync<ReservationValidationException>(() => service.CreateScheduleBlockAsync(Request() with { StartTime = new TimeOnly(11, 30) }, "admin", default));
        await service.CreateScheduleBlockAsync(Request("current") with { StartTime = new TimeOnly(12, 0), DurationMinutes = 30 }, "admin", default);
        await context.Database.GetCollection<ResourceRecord>("resources").UpdateOneAsync(x => x.Id == "bay-1", Builders<ResourceRecord>.Update.Set(x => x.Active, false));
        await Assert.ThrowsAsync<ReservationValidationException>(() => service.CreateScheduleBlockAsync(Request(), "admin", default));
    }

    [Theory]
    [InlineData("bay-1")]
    [InlineData("*")]
    public async Task ConcurrentBlockAndBookingHaveOneWinner(string resource)
    {
        var (store, context) = await Setup();
        async Task<bool> Block() { try { await Service(store, context).CreateScheduleBlockAsync(Request() with { ResourceId = resource }, "admin", default); return true; } catch (ReservationValidationException) { return false; } }
        async Task<bool> Book() { try { await store.InsertAsync(Booking("Confirmed"), default); return true; } catch (AllocationConflictException) { return false; } }
        Assert.Single(await Task.WhenAll(Block(), Book()), x => x);
    }

    [Fact]
    public async Task ConcurrentReplaysSaveOneBlock()
    {
        var (store, context) = await Setup();
        var service = Service(store, context);
        var ids = await Task.WhenAll(service.CreateScheduleBlockAsync(Request(), "admin", default), service.CreateScheduleBlockAsync(Request(), "admin", default));
        Assert.Equal(ids[0], ids[1]);
        Assert.Equal(1, await context.Database.GetCollection<ScheduleBlock>("schedule_blocks").CountDocumentsAsync(FilterDefinition<ScheduleBlock>.Empty));
    }

    [Fact]
    public async Task MigrationPreservesLegacyBlocksAndValidatesAuditFieldsOnRerun()
    {
        var (_, context) = await Setup();
        await new AccountsMigration().UpAsync(context.Database, default);
        await new Migration_004_BookingProcess().UpAsync(context.Database, default);
        await new Migration_005_GoogleAccounts().UpAsync(context.Database, default);
        await new Migration_006_SchemaHardening().UpAsync(context.Database, default);
        var blocks = context.Database.GetCollection<ScheduleBlock>("schedule_blocks");
        await blocks.InsertOneAsync(new() { Id = "legacy", StartUtc = Start.AddDays(-1), EndUtc = Start.AddDays(-1).AddHours(1) });
        var migration = new Migration_011_AdminScheduleBlocks();
        await migration.UpAsync(context.Database, default);
        await migration.UpAsync(context.Database, default);
        var releaseMigration = new Migration_012_ReleaseScheduleBlocks();
        await releaseMigration.UpAsync(context.Database, default);
        await releaseMigration.UpAsync(context.Database, default);
        await Assert.ThrowsAsync<MongoWriteException>(() => blocks.UpdateOneAsync(x => x.Id == "legacy",
            Builders<ScheduleBlock>.Update.Set(x => x.ReleasedUtc, Start)));
        Assert.Null((await blocks.Find(x => x.Id == "legacy").SingleAsync()).Reason);
        await Assert.ThrowsAsync<MongoWriteException>(() => blocks.InsertOneAsync(new() { Id = "bad", Reason = "", StartUtc = Start, EndUtc = Start.AddHours(1) }));
        var service = Service(new ReservationPersistence(context), context);
        var id = await service.CreateScheduleBlockAsync(Request() with { ResourceId = "bay-2" }, "admin", default);
        Assert.True(await service.ReleaseScheduleBlockAsync(id, "Finished", "admin", default));
    }
    [Fact]
    public async Task ReleasePreservesAuditAndFreesEveryCapacityQueryWithoutReactivation()
    {
        var (store, context) = await Setup();
        var clock = new Clock();
        var service = Service(store, context, clock);
        var id = await service.CreateScheduleBlockAsync(Request(), "creator", default);
        Assert.Contains("bay-1", await store.OccupiedAsync(Start, Start.AddHours(1), default));
        Assert.True(await service.ReleaseScheduleBlockAsync(id, "  Maintenance finished  ", "releaser", default));
        clock.Now = clock.Now.AddMinutes(5);
        Assert.True(await service.ReleaseScheduleBlockAsync(id, "Different retry", "other-admin", default));
        var saved = (await store.BlockByIdAsync(id, default))!;
        Assert.Equal("creator", saved.CreatedBy);
        Assert.Equal("Maintenance", saved.Reason);
        Assert.Equal(Start, saved.StartUtc);
        Assert.Equal(Start.AddHours(1), saved.EndUtc);
        Assert.Equal("releaser", saved.ReleasedBy);
        Assert.Equal("Maintenance finished", saved.ReleaseReason);
        Assert.Equal(clock.Now.AddMinutes(-5).UtcDateTime, saved.ReleasedUtc);
        Assert.DoesNotContain("bay-1", await store.OccupiedAsync(Start, Start.AddHours(1), default));
        Assert.DoesNotContain((await service.GetStaffScheduleAsync(Day, default)).SelectMany(x => x.Entries), x => x.BlockId == id);
        await store.InsertAsync(Booking("Confirmed"), default);
        var replay = await Assert.ThrowsAsync<ReservationValidationException>(() => service.CreateScheduleBlockAsync(Request(), "creator", default));
        Assert.Contains("released", replay.Message);
        Assert.NotNull(await service.GetScheduleBlockAsync(id, default));
        Assert.False(await service.ReleaseScheduleBlockAsync("missing", "Finished", "admin", default));
        Assert.Null(await service.GetScheduleBlockAsync("missing", default));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task LegacyClosuresReleaseWithoutAffectingOtherBlocks(bool venueWide)
    {
        var (store, context) = await Setup();
        var blocks = context.Database.GetCollection<ScheduleBlock>("schedule_blocks");
        await blocks.InsertManyAsync([
            new() { Id = "legacy", ResourceIds = venueWide ? [] : ["bay-1"], StartUtc = Start.AddDays(-1), EndUtc = Start.AddDays(1) },
            new() { Id = "other", ResourceIds = ["bay-2"], StartUtc = Start, EndUtc = Start.AddHours(1) }
        ]);
        Assert.True(await Service(store, context).ReleaseScheduleBlockAsync("legacy", "Reopened", "admin", default));
        Assert.Equal(new[] { "bay-2" }, await store.OccupiedAsync(Start, Start.AddHours(1), default));
        Assert.Null((await store.BlockByIdAsync("legacy", default))!.CreatedBy);
        await store.InsertAsync(Booking("Pending"), default);
    }

    [Fact]
    public async Task ConcurrentReleasesKeepOneAuditAndConcurrentBookingNeverBypassesOtherBlocks()
    {
        var (store, context) = await Setup();
        var service = Service(store, context);
        var id = await service.CreateScheduleBlockAsync(Request(), "creator", default);
        var releases = await Task.WhenAll(service.ReleaseScheduleBlockAsync(id, "First", "one", default),
            service.ReleaseScheduleBlockAsync(id, "Second", "two", default));
        Assert.All(releases, Assert.True);
        var saved = (await store.BlockByIdAsync(id, default))!;
        Assert.True(saved.ReleasedBy == "one" && saved.ReleaseReason == "First" || saved.ReleasedBy == "two" && saved.ReleaseReason == "Second");
        var newId = await service.CreateScheduleBlockAsync(Request("replacement"), "creator", default);
        async Task<bool> Book() { try { await store.InsertAsync(Booking("Confirmed"), default); return true; } catch (AllocationConflictException) { return false; } }
        var booking = Book();
        await service.ReleaseScheduleBlockAsync(id, "Stale retry", "one", default);
        Assert.False(await booking);
        Assert.Null((await store.BlockByIdAsync(newId, default))!.ReleasedUtc);
    }

    [Fact]
    public async Task ReleaseRacingABookingKeepsStateConsistent()
    {
        var (store, context) = await Setup();
        var service = Service(store, context);
        var id = await service.CreateScheduleBlockAsync(Request(), "creator", default);
        async Task<bool> Book() { try { await store.InsertAsync(Booking("Confirmed"), default); return true; } catch (AllocationConflictException) { return false; } }
        var booking = Book();
        Assert.True(await service.ReleaseScheduleBlockAsync(id, "Finished", "admin", default));
        var booked = await booking;
        Assert.NotNull((await store.BlockByIdAsync(id, default))!.ReleasedUtc);
        if (!booked) { await store.InsertAsync(Booking("Confirmed"), default); }
        Assert.Contains("bay-1", await store.OccupiedAsync(Start, Start.AddHours(1), default));
    }

}
