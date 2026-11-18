using HoleInHub.Data.Reservations;
using HoleInHub.Tests.Integration.Fixtures;
using MongoDB.Driver;
using Xunit;

namespace HoleInHub.Tests.Integration.Reservations;

[Trait("Category", "Integration")]
public sealed class OutboxRecoveryTests(MongoDbFixture fixture) : IClassFixture<MongoDbFixture>
{
    [Fact]
    public async Task DeliveryListSeparatesSuppressedMailAndRejectsExpiredOrConcurrentRetries()
    {
        Assert.True(fixture.IsServerAvailable, "Start the MongoDB replica set.");
        var context = fixture.CreateDbContext();
        var outbox = new OutboxPersistence(context);
        var now = DateTime.UtcNow;
        await outbox.EnqueueAsync(new() { Id = "obsolete", DueUtc = now, ExpiresUtc = now.AddMinutes(-1) }, default);
        var claimed = (await outbox.ClaimAsync(now, default))!;
        Assert.False(await outbox.IsEligibleAsync(claimed, default, now));
        await outbox.SuppressAsync(claimed.Id, claimed.LeaseToken!, now, default);
        var suppressed = Assert.Single(await outbox.ListDeliveryAsync("suppressed", 0, 20, now, default));
        Assert.Equal("obsolete", suppressed.Id);
        Assert.Null(suppressed.SentUtc);
        Assert.False(await outbox.RetryDeliveryAsync("obsolete", now, default));
        Assert.Null(await outbox.ClaimAsync(now, default));

        await outbox.EnqueueAsync(new() { Id = "expired-code", Body = "protected:secret", DueUtc = now, ExpiresUtc = now, Failed = true }, default);
        await outbox.EnqueueAsync(new() { Id = "legacy-code", Body = "protected:secret", DueUtc = now, Failed = true }, default);
        await outbox.EnqueueAsync(new() { Id = "failed", Body = "Private content", DueUtc = now, Failed = true }, default);
        Assert.False(await outbox.RetryDeliveryAsync("expired-code", now, default));
        Assert.False(await outbox.RetryDeliveryAsync("legacy-code", now, default));
        Assert.Equal(3, (await outbox.ListDeliveryAsync("failed", 0, 20, now, default)).Count);
        var retries = await Task.WhenAll(outbox.RetryDeliveryAsync("failed", now, default), outbox.RetryDeliveryAsync("failed", now, default));
        Assert.Single(retries, x => x);
        Assert.Equal("failed", Assert.Single(await outbox.ListDeliveryAsync("queued", 0, 20, now, default)).Id);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExhaustedDeliveryIsVisibleAndCanBeReplayed(bool workerCrashed)
    {
        Assert.True(fixture.IsServerAvailable, "Start the MongoDB replica set.");
        var context = fixture.CreateDbContext();
        var messages = context.Database.GetCollection<OutboxRecord>("outbox");
        var outbox = new OutboxPersistence(context);
        var now = DateTime.UtcNow;
        await outbox.EnqueueAsync(new OutboxRecord { Id = "exhausted", Recipient = "customer@example.test", DueUtc = now, Attempts = 4 }, default);
        var last = await outbox.ClaimAsync(now, default);
        Assert.NotNull(last);
        if (!workerCrashed) { await outbox.RetryAsync(last.Id, last.LeaseToken!, now, default); }
        Assert.Null(await outbox.ClaimAsync(now.AddMinutes(3), default));
        var failed = await messages.Find(x => x.Id == last.Id).SingleAsync();
        Assert.True(failed.Failed);
        Assert.Null(failed.SentUtc);
        Assert.True(await outbox.ReplayFailedAsync(last.Id, now.AddMinutes(3), default));
        var replayed = await outbox.ClaimAsync(now.AddMinutes(3), default);
        Assert.NotNull(replayed);
        Assert.Equal(1, replayed.Attempts);
        await outbox.CompleteAsync(replayed.Id, replayed.LeaseToken!, default);
        Assert.False(await outbox.ReplayFailedAsync(last.Id, now.AddMinutes(4), default));
    }
}
