using HoleInHub.Data.Reservations;
using HoleInHub.Tests.Integration.Fixtures;
using MongoDB.Driver;
using Xunit;

namespace HoleInHub.Tests.Integration;

[Trait("Category", "Integration")]
public sealed class CustomerInboxActionsTests(MongoDbFixture fixture) : IClassFixture<MongoDbFixture>
{
    [Fact]
    public async Task InboxActionsAreOwnedPersistAcrossQueriesAndLeaveBookingHistoryIntact()
    {
        var context = fixture.CreateDbContext();
        var collection = context.Database.GetCollection<ReservationRecord>("reservations");
        var persistence = new ReservationPersistence(context);
        var now = new DateTime(DateTime.UtcNow.Ticks / TimeSpan.TicksPerMillisecond * TimeSpan.TicksPerMillisecond, DateTimeKind.Utc);
        var history = new List<HistoryRecord> { new() { AtUtc = now.AddDays(-1) } };
        history.AddRange(Enumerable.Range(1, 30).Select(i => new HistoryRecord { EventType = "SubmissionCreated", AtUtc = now.AddMinutes(-i) }));
        history.Add(new() { EventType = "SubmissionCreated", AtUtc = now.AddDays(1) });
        var owner = new ReservationRecord { Id = "owner-booking", AccountId = "owner", Reference = "HIH-OWNER", History = history };
        var other = new ReservationRecord { Id = "other-booking", AccountId = "other", Reference = "HIH-OTHER", History = [new() { EventType = "SubmissionCreated", AtUtc = now }] };
        await collection.InsertManyAsync([owner, other]);
        string[] types = ["SubmissionCreated"];
        Assert.Equal(25, (await persistence.ReadCustomerNotificationsAsync("owner", types, 0, 25, default)).Length);
        await persistence.MarkCustomerNotificationsReadAsync("owner", types, [new(owner.Id, 1, now.AddMinutes(-1))], default);
        await persistence.DismissCustomerNotificationsAsync("other", types, [new(owner.Id, 2, now.AddMinutes(-2))], default);
        Assert.Equal(31, (await persistence.ReadCustomerNotificationsAsync("owner", types, 0, 100, default)).Length);
        await persistence.DismissCustomerNotificationsAsync("owner", types, [new(owner.Id, 2, now.AddMinutes(-2))], default);
        Assert.Equal(29, (await persistence.ReadCustomerNotificationsAsync("owner", types, 0, 100, default, true)).Length);
        await persistence.MarkAllCustomerNotificationsReadAsync("owner", types, now, default);
        var remaining = await persistence.ReadCustomerNotificationsAsync("owner", types, 0, 100, default, true);
        Assert.Single(remaining);
        Assert.Equal(31, remaining[0].Index);
        Assert.True(await persistence.HasUnreadCustomerNotificationsAsync("other", types, default));
        await persistence.MarkAllCustomerNotificationsReadAsync("owner", types, now.AddDays(2), default);
        Assert.False(await persistence.HasUnreadCustomerNotificationsAsync("owner", types, default));
        var saved = await collection.Find(x => x.Id == owner.Id).SingleAsync();
        Assert.Equal(32, saved.History.Count);
        Assert.Contains(2, saved.CustomerDismissedHistory!);
    }
}
