using HoleInHub.Data.Migrations.Definitions;
using HoleInHub.Data.Reservations;
using HoleInHub.Tests.Integration.Fixtures;
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
    private static ReservationRecord Request(string[] ids, string service = "Regular") => new() { AccountId = Guid.NewGuid().ToString("N"), Email = "customer@example.test", IdempotencyKey = Guid.NewGuid().ToString("N"), PayloadHash = "test", StartUtc = new DateTime(2026, 11, 3, 4, 0, 0, DateTimeKind.Utc), EndUtc = new DateTime(2026, 11, 3, 5, 0, 0, DateTimeKind.Utc), CreatedUtc = DateTime.UtcNow, ReviewDeadlineUtc = DateTime.UtcNow.AddMinutes(30), ResourceIds = ids, Service = service };
    [Fact]
    public async Task ConcurrentOverlapsHaveOneWinner()
    {
        var (store, _) = await Setup(); async Task<bool> Attempt() { try { await store.InsertAsync(Request(["bay-1"]), default); return true; } catch (AllocationConflictException) { return false; } }
        var results = await Task.WhenAll(Attempt(), Attempt()); Assert.Single(results, x => x);
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
        var (store, context) = await Setup(); var request = Request(["bay-1"]); request.ReviewDeadlineUtc = DateTime.UtcNow.AddMinutes(-1); await store.InsertAsync(request, default); await store.ExpireAsync(DateTime.UtcNow, ["staff@example.test"], default); await store.ExpireAsync(DateTime.UtcNow, ["staff@example.test"], default); Assert.Equal("Expired", (await store.OwnedAsync(request.AccountId, request.Id, default))!.Status); Assert.Equal(2, await context.Database.GetCollection<OutboxRecord>("outbox").CountDocumentsAsync(FilterDefinition<OutboxRecord>.Empty)); await store.InsertAsync(Request(["bay-1"]), default);
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
        var (store, context) = await Setup(); var outbox = new OutboxPersistence(context); var request = Request(["bay-1"]); request.CreatedUtc = DateTime.UtcNow.AddMinutes(-26); request.ReviewDeadlineUtc = DateTime.UtcNow.AddMinutes(4); await store.InsertAsync(request, default);
        var service = new HoleInHub.Business.Reservations.ReservationService(store, outbox, Microsoft.Extensions.Options.Options.Create(new HoleInHub.Business.Reservations.ReservationPolicy { StaffEmails = ["staff@example.test"] })); await service.ProcessPendingAsync(default); await service.ProcessPendingAsync(default);
        var message = Assert.Single(await context.Database.GetCollection<OutboxRecord>("outbox").Find(FilterDefinition<OutboxRecord>.Empty).ToListAsync()); Assert.Contains("review25", message.Id); Assert.True(await outbox.IsEligibleAsync(message, default)); await store.ExpireAsync(DateTime.UtcNow.AddMinutes(5), ["staff@example.test"], default); Assert.False(await outbox.IsEligibleAsync(message, default));
    }
    [Fact]
    public async Task ClosureRejectsSubmission()
    {
        var (store, context) = await Setup(); var request = Request(["bay-1"]); await context.Database.GetCollection<ScheduleBlock>("schedule_blocks").InsertOneAsync(new ScheduleBlock { Id = "closure", StartUtc = request.StartUtc, EndUtc = request.EndUtc }); await Assert.ThrowsAsync<AllocationConflictException>(() => store.InsertAsync(request, default));
    }
}


