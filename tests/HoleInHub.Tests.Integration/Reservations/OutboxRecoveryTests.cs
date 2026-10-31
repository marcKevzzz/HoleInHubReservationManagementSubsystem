using HoleInHub.Data.Reservations;
using HoleInHub.Tests.Integration.Fixtures;
using MongoDB.Driver;
using Xunit;

namespace HoleInHub.Tests.Integration.Reservations;

[Trait("Category", "Integration")]
public sealed class OutboxRecoveryTests(MongoDbFixture fixture) : IClassFixture<MongoDbFixture>
{
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
