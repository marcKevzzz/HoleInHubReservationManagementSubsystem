using HoleInHub.Business.Reservations;
using HoleInHub.Data.Migrations.Definitions;
using HoleInHub.Data.Reservations;
using HoleInHub.Tests.Integration.Fixtures;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Xunit;
namespace HoleInHub.Tests.Integration.Reservations;

[Trait("Category", "Integration")]
public sealed class AllocationTests(MongoDbFixture fixture) : IClassFixture<MongoDbFixture>
{
    private async Task<(ReservationPersistence Store, HoleInHub.Data.Context.IMongoDbContext Context)> Setup()
    {
        Assert.True(fixture.IsServerAvailable, "Start the MongoDB replica set."); var context = fixture.CreateDbContext(); await new Migration_002_Reservations().UpAsync(context.Database, default); return (new ReservationPersistence(context), context);
    }
    private static ReservationRecord Request(string[] ids, string service = "Regular") => new() { AccountId = Guid.NewGuid().ToString("N"), Email = "customer@example.test", IdempotencyKey = Guid.NewGuid().ToString("N"), SubmissionHash = "test", StartUtc = new DateTime(2026, 11, 3, 4, 0, 0, DateTimeKind.Utc), EndUtc = new DateTime(2026, 11, 3, 5, 0, 0, DateTimeKind.Utc), CreatedUtc = DateTime.UtcNow, ReviewDeadlineUtc = DateTime.UtcNow.AddMinutes(30), ResourceIds = ids, Service = service };
    [Fact]
    public async Task StaffScheduleUsesManilaDayAndShowsSharedBookingsAndTargetedBlocks()
    {
        var (store, context) = await Setup();
        var service = new ReservationService(store, new OutboxPersistence(context), Options.Create(new ReservationPolicy()));
        var booking = Request(["bay-1", "bay-2"]);
        booking.Reference = "HIH-SCHEDULE";
        booking.Status = "Confirmed";
        var day = new DateOnly(2026, 11, 3);
        var midnightUtc = new DateTime(2026, 11, 2, 16, 0, 0, DateTimeKind.Utc);
        await context.Database.GetCollection<ReservationRecord>("reservations").InsertOneAsync(booking);
        await context.Database.GetCollection<ScheduleBlock>("schedule_blocks").InsertManyAsync([
            new() { Id = "ends-at-midnight", StartUtc = midnightUtc.AddHours(-1), EndUtc = midnightUtc },
            new() { Id = "bay-one-maintenance", ResourceIds = ["bay-1"], StartUtc = midnightUtc.AddHours(1), EndUtc = midnightUtc.AddHours(2) },
            new() { Id = "venue-closure", StartUtc = midnightUtc.AddHours(2), EndUtc = midnightUtc.AddHours(3) }
        ]);

        var schedule = await service.GetStaffScheduleAsync(day, default);
        var shared = schedule.Where(bay => bay.Entries.Any(entry => entry.ReservationId == booking.Id)).ToArray();
        Assert.Equal(2, shared.Length);
        Assert.Equal(new[] { 1, 2 }, shared[0].Entries.Where(entry => entry.Status == "Blocked").Select(entry => entry.Start.Hour));
        Assert.Equal(new[] { 2 }, shared[1].Entries.Where(entry => entry.Status == "Blocked").Select(entry => entry.Start.Hour));
        Assert.Equal(booking.Id, await service.FindStaffBookingAsync(booking.Reference, default));
        Assert.Null(await service.FindStaffBookingAsync("HIH-SCHED", default));
    }

    [Fact]
    public async Task SubmissionReceiptIsDeliverableBeforeAndAfterStaffChangesTheStatus()
    {
        var (store, context) = await Setup();
        var request = Request(["bay-1"]);
        request.StartUtc = DateTime.UtcNow.AddHours(2);
        request.EndUtc = request.StartUtc.AddHours(1);
        await store.InsertAsync(request, default);
        var receipt = await context.Database.GetCollection<OutboxRecord>("outbox").Find(x => x.Id == request.Id + ":submitted").SingleAsync();
        var outbox = new OutboxPersistence(context);
        Assert.Null(receipt.RequiredStatus);
        Assert.True(await outbox.IsEligibleAsync(receipt, default));
        Assert.True(await store.TransitionAsync(request.Id, "approve", "staff", DateTime.UtcNow, [], default));
        Assert.True(await outbox.IsEligibleAsync(receipt, default));
        var staleAlert = new OutboxRecord { ReservationId = request.Id, RequiredStatus = "Pending" };
        Assert.False(await outbox.IsEligibleAsync(staleAlert, default));
        Assert.False(await outbox.IsEligibleAsync(new OutboxRecord { ReservationId = "missing" }, default));
    }

    [Fact]
    public async Task AssistedBookingConfirmsWithoutCustomerOwnershipAndCannotDoubleBook()
    {
        var (store, context) = await Setup();
        await new Migration_004_BookingProcess().UpAsync(context.Database, default);
        await new AccountsMigration().UpAsync(context.Database, default);
        await new Migration_005_GoogleAccounts().UpAsync(context.Database, default);
        await new Migration_006_SchemaHardening().UpAsync(context.Database, default);
        await new Migration_010_StaffAssistedReservations().UpAsync(context.Database, default);
        var service = new ReservationService(store, new OutboxPersistence(context), Options.Create(new ReservationPolicy()));
        var day = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(2));
        var request = new StaffBookingRequest("bay-2", day, new TimeOnly(13, 0), 60, "Alex", "Santos", "09175550123", 4, "Walk-in", "front-desk-test");

        var saved = await service.CreateStaffBookingAsync(request, "employee", default);
        var record = (await store.ByIdAsync(saved.Id, default))!;
        Assert.Equal("Confirmed", record.Status);
        Assert.Equal("employee", Assert.Single(record.History).Actor);
        Assert.Equal("Walk-in", record.BookingSource);
        Assert.Null(record.CustomerAccountId);
        Assert.Null(record.AccessTokenHash);
        Assert.Null(await store.OwnedAsync("employee", saved.Id, default));
        Assert.Equal(0, await context.Database.GetCollection<OutboxRecord>("outbox").CountDocumentsAsync(FilterDefinition<OutboxRecord>.Empty));
        Assert.Equal(saved.Id, (await service.CreateStaffBookingAsync(request, "employee", default)).Id);
        await Assert.ThrowsAsync<ReservationValidationException>(() => service.CreateStaffBookingAsync(request with { Attendees = 5 }, "employee", default));
        await Assert.ThrowsAsync<ReservationValidationException>(() => service.CreateStaffBookingAsync(request with { IdempotencyKey = "other" }, "other-employee", default));
        var publicRecord = Request(["bay-3"]);
        publicRecord.Email = "";
        publicRecord.Reference = "HIH-MISSING-EMAIL";
        publicRecord.Attendees = 1;
        await Assert.ThrowsAsync<MongoWriteException>(() => store.InsertAsync(publicRecord, default));
    }

    [Fact]
    public async Task ConcurrentOverlapsHaveOneWinner()
    {
        var (store, _) = await Setup(); async Task<bool> Attempt() { try { await store.InsertAsync(Request(["bay-1"]), default); return true; } catch (AllocationConflictException) { return false; } }
        var results = await Task.WhenAll(Attempt(), Attempt()); Assert.Single(results, x => x);
    }
    [Fact]
    public async Task RetiringBayAndTakingFutureBookingCannotBothSucceed()
    {
        var (store, _) = await Setup();
        var now = DateTime.UtcNow;
        var request = Request(["bay-1"]);
        request.StartUtc = now.AddDays(1);
        request.EndUtc = request.StartUtc.AddHours(1);
        async Task<bool> Book()
        {
            try { await store.InsertAsync(request, default); return true; }
            catch (AllocationConflictException) { return false; }
        }
        async Task<bool> Retire()
        {
            try { return await store.SetResourceActiveAsync("bay-1", false, now, default); }
            catch (AllocationConflictException) { return false; }
        }
        var results = await Task.WhenAll(Book(), Retire());
        Assert.Single(results, success => success);
        Assert.Equal(results[0], (await store.ReadResourceDirectoryAsync(default)).Single(x => x.Id == "bay-1").Active);
    }

    [Fact]
    public async Task EditingBayPreservesIdRejectsStaleEditsAndProtectsFutureAllocations()
    {
        var (store, _) = await Setup();
        var now = DateTime.UtcNow;
        Assert.True(await store.EditResourceAsync(new() { Id = "bay-1", Name = "Practice bay", Service = "Regular" }, 0, now, default));
        var renamed = (await store.ReadResourceDirectoryAsync(default)).Single(x => x.Id == "bay-1");
        Assert.Equal("Practice bay", renamed.Name);
        await Assert.ThrowsAsync<AllocationConflictException>(() => store.EditResourceAsync(new() { Id = "bay-1", Name = "Stale", Service = "Regular" }, 0, now, default));
        var request = Request(["bay-1"]);
        request.StartUtc = now.AddDays(1);
        request.EndUtc = request.StartUtc.AddHours(1);
        await store.InsertAsync(request, default);
        await Assert.ThrowsAsync<AllocationConflictException>(() => store.EditResourceAsync(new() { Id = "bay-1", Name = "VIP practice", Service = "VIP" }, 1, now, default));
        Assert.True(await store.EditResourceAsync(new() { Id = "bay-1", Name = "Practice room", Service = "Regular", MaxPlayers = 4 }, 1, now, default));
        Assert.True(await store.EditResourceAsync(new() { Id = "bay-2", Name = "VIP room", Service = "VIP" }, 0, now, default));
        Assert.False(await store.EditResourceAsync(new() { Id = "missing", Name = "Missing", Service = "VIP" }, 0, now, default));
    }
    [Fact]
    public async Task MultiBayFailureDoesNotPartiallyHold()
    {
        var (store, _) = await Setup(); await store.InsertAsync(Request(["bay-1"]), default); await Assert.ThrowsAsync<AllocationConflictException>(() => store.InsertAsync(Request(["bay-1", "bay-2"]), default)); await store.InsertAsync(Request(["bay-2"]), default);
    }
    [Fact]
    public async Task WholeShopBlocksVipAndRegular()
    {
        var (store, _) = await Setup(); await store.InsertAsync(Request([], "WholeShop"), default); await Assert.ThrowsAsync<AllocationConflictException>(() => store.InsertAsync(Request(["vip-7"], "VIP"), default)); await Assert.ThrowsAsync<AllocationConflictException>(() => store.InsertAsync(Request(["bay-1"]), default));
    }
    [Fact]
    public async Task RetriesReturnOriginalReservation()
    {
        var (store, _) = await Setup(); var request = Request(["bay-1"]); var first = await store.InsertAsync(request, default); var second = await store.InsertAsync(request, default); Assert.Equal(first.Id, second.Id);
    }
    [Fact]
    public async Task ExpiryReleasesCapacityAndWritesNotificationsOnce()
    {
        var (store, context) = await Setup(); var request = Request(["bay-1"]); request.ReviewDeadlineUtc = DateTime.UtcNow.AddMinutes(-1); await store.InsertAsync(request, default); await store.ExpireAsync(request.StartUtc, ["staff@example.test"], default); await store.ExpireAsync(request.StartUtc, ["staff@example.test"], default); Assert.Equal("Expired", (await store.OwnedAsync(request.AccountId, request.Id, default))!.Status); Assert.Equal(3, await context.Database.GetCollection<OutboxRecord>("outbox").CountDocumentsAsync(FilterDefinition<OutboxRecord>.Empty)); await store.InsertAsync(Request(["bay-1"]), default);
    }
    [Fact]
    public async Task CatalogMutationContendsWithWholeShop()
    {
        var (store, _) = await Setup(); var request = Request([], "WholeShop"); request.StartUtc = DateTime.UtcNow.AddDays(1); request.EndUtc = request.StartUtc.AddHours(1);
        async Task<bool> Book() { try { await store.InsertAsync(request, default); return true; } catch (AllocationConflictException) { return false; } }
        async Task<bool> Add() { try { await store.AddResourceAsync(new ResourceRecord { Id = "private", Name = "Private", Service = "Private" }, default); return true; } catch (AllocationConflictException) { return false; } }
        var results = await Task.WhenAll(Book(), Add()); Assert.True(results[0]); if (results[1]) { Assert.Contains("private", (await store.OwnedAsync(request.AccountId, request.Id, default))!.ResourceIds); }
    }
    [Fact]
    public async Task OutboxClaimsAreExclusiveAndRetryable()
    {
        var (_, context) = await Setup(); var outbox = new OutboxPersistence(context); var now = DateTime.UtcNow; await outbox.EnqueueAsync(new OutboxRecord { Id = "email", Recipient = "customer@example.test", DueUtc = now }, default);
        var claims = await Task.WhenAll(outbox.ClaimAsync(now, default), outbox.ClaimAsync(now, default)); var claimed = Assert.Single(claims, x => x != null)!;
        await outbox.RetryAsync(claimed.Id, claimed.LeaseToken!, now, default); Assert.Null(await outbox.ClaimAsync(now, default)); var retried = await outbox.ClaimAsync(now.AddMinutes(3), default); Assert.NotNull(retried); await outbox.CompleteAsync(retried.Id, retried.LeaseToken!, default); Assert.Null(await outbox.ClaimAsync(now.AddHours(1), default));
    }
    [Fact]
    public async Task PendingAlertsAreDeduplicatedAndStaleAlertsSuppressed()
    {
        var (store, context) = await Setup(); var outbox = new OutboxPersistence(context); var request = Request(["bay-1"]); request.CreatedUtc = DateTime.UtcNow.AddMinutes(-26); request.ReviewDeadlineUtc = DateTime.UtcNow.AddMinutes(4);
        request.StartUtc = DateTime.UtcNow.AddHours(2); request.EndUtc = request.StartUtc.AddHours(1); await store.InsertAsync(request, default);
        var service = new HoleInHub.Business.Reservations.ReservationService(store, outbox, Microsoft.Extensions.Options.Options.Create(new HoleInHub.Business.Reservations.ReservationPolicy { StaffEmails = ["staff@example.test"] })); await service.ProcessPendingAsync(default); await service.ProcessPendingAsync(default);
        var message = Assert.Single(await context.Database.GetCollection<OutboxRecord>("outbox").Find(x => x.RequiredStatus == "Pending").ToListAsync()); Assert.Contains("review25", message.Id); Assert.True(await outbox.IsEligibleAsync(message, default)); await store.ExpireAsync(request.StartUtc, ["staff@example.test"], default); Assert.False(await outbox.IsEligibleAsync(message, default));
    }
    [Fact]
    public async Task ClosureRejectsSubmission()
    {
        var (store, context) = await Setup(); var request = Request(["bay-1"]); await context.Database.GetCollection<ScheduleBlock>("schedule_blocks").InsertOneAsync(new ScheduleBlock { Id = "closure", StartUtc = request.StartUtc, EndUtc = request.EndUtc }); await Assert.ThrowsAsync<AllocationConflictException>(() => store.InsertAsync(request, default));
    }
}


