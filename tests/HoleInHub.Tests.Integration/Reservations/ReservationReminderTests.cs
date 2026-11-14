using HoleInHub.Business.Reservations;
using HoleInHub.Data.Reservations;
using HoleInHub.Tests.Integration.Fixtures;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Xunit;

namespace HoleInHub.Tests.Integration.Reservations;

[Trait("Category", "Integration")]
public sealed class ReservationReminderTests(MongoDbFixture fixture) : IClassFixture<MongoDbFixture>
{
    private sealed class ReminderTime(DateTime now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(now);
    }

    private static ReservationRecord Booking(DateTime start) => new()
    {
        Reference = "HIH-REMINDER", Status = "Confirmed", Email = "customer@example.test",
        Service = "Regular", StartUtc = start, EndUtc = start.AddHours(1), CreatedUtc = start.AddHours(-2)
    };

    [Fact]
    public async Task ConcurrentWorkerScansQueueOneReminderPerCurrentScheduleAndCatchLateConfirmation()
    {
        Assert.True(fixture.IsServerAvailable, "Start the local MongoDB replica set.");
        var context = fixture.CreateDbContext();
        var now = DateTime.UtcNow;
        // BSON dates have millisecond precision.
        now = new DateTime(now.Ticks - now.Ticks % TimeSpan.TicksPerMillisecond, DateTimeKind.Utc);
        var reservations = context.Database.GetCollection<ReservationRecord>("reservations");
        var messages = context.Database.GetCollection<OutboxRecord>("outbox");
        var atBoundary = Booking(now.AddMinutes(15));
        var late = Booking(now.AddMinutes(3));
        late.Status = "Pending";
        late.ReviewDeadlineUtc = now.AddMinutes(2);
        var noEmail = Booking(now.AddMinutes(5));
        noEmail.Email = "";
        var started = Booking(now);
        var tomorrow = Booking(now.AddDays(1));
        var tooEarly = Booking(now.AddMinutes(15).AddMilliseconds(1));
        var cancelled = Booking(now.AddMinutes(5));
        cancelled.Status = "Cancelled";
        var checkedIn = Booking(now.AddMinutes(5));
        checkedIn.Status = "Checked-In";
        var cancelling = Booking(now.AddMinutes(5));
        cancelling.CancellationRequested = true;
        await reservations.InsertManyAsync([atBoundary, late, noEmail, started, tomorrow, tooEarly, cancelled, checkedIn, cancelling]);
        ReservationService Worker() => new(new ReservationPersistence(context), new OutboxPersistence(context),
            Options.Create(new ReservationPolicy()), new ReminderTime(now));
        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Worker().ProcessPendingAsync(default)));
        Assert.Single(await messages.Find(x => x.ReminderStartUtc != null).ToListAsync());
        await reservations.UpdateOneAsync(x => x.Id == late.Id, Builders<ReservationRecord>.Update.Set(x => x.Status, "Confirmed"));
        await Worker().ProcessPendingAsync(default);
        Assert.Equal(2, await messages.CountDocumentsAsync(x => x.ReminderStartUtc != null));
        var lateMessage = await messages.Find(x => x.ReservationId == late.Id).SingleAsync();
        Assert.Equal(now, lateMessage.DueUtc);
        await reservations.UpdateOneAsync(x => x.Id == atBoundary.Id, Builders<ReservationRecord>.Update.Set(x => x.StartUtc, now.AddMinutes(10)));
        await Worker().ProcessPendingAsync(default);
        Assert.Equal(3, await messages.CountDocumentsAsync(x => x.ReminderStartUtc != null));
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("checkin")]
    [InlineData("reschedule")]
    [InlineData("request-cancellation")]
    [InlineData("email")]
    [InlineData("start")]
    public async Task RetryRechecksLatestReservationBeforeDelivery(string change)
    {
        Assert.True(fixture.IsServerAvailable, "Start the local MongoDB replica set.");
        var context = fixture.CreateDbContext();
        var now = DateTime.UtcNow;
        now = new DateTime(now.Ticks - now.Ticks % TimeSpan.TicksPerMillisecond, DateTimeKind.Utc);
        var reservation = Booking(now.AddMinutes(10));
        var reservations = context.Database.GetCollection<ReservationRecord>("reservations");
        await reservations.InsertOneAsync(reservation);
        var outbox = new OutboxPersistence(context);
        await outbox.EnqueueAsync(ReservationReminderPolicy.Create(reservation, now)!, default);
        var claimed = (await outbox.ClaimAsync(now, default))!;
        Assert.True(await outbox.IsEligibleAsync(claimed, default, now));
        await outbox.RetryAsync(claimed.Id, claimed.LeaseToken!, now, default);
        var update = change switch
        {
            "cancel" => Builders<ReservationRecord>.Update.Set(x => x.Status, "Cancelled"),
            "checkin" => Builders<ReservationRecord>.Update.Set(x => x.CheckedInUtc, now),
            "reschedule" => Builders<ReservationRecord>.Update.Set(x => x.StartUtc, reservation.StartUtc.AddMinutes(1)),
            "request-cancellation" => Builders<ReservationRecord>.Update.Set(x => x.CancellationRequested, true),
            "email" => Builders<ReservationRecord>.Update.Set(x => x.Email, "changed@example.test"),
            _ => Builders<ReservationRecord>.Update.Set(x => x.Reference, reservation.Reference)
        };
        await reservations.UpdateOneAsync(x => x.Id == reservation.Id, update);
        var retryTime = change == "start" ? reservation.StartUtc : now.AddMinutes(2);
        var retried = (await outbox.ClaimAsync(retryTime, default))!;
        Assert.Equal(claimed.Id, retried.Id);
        Assert.False(await outbox.IsEligibleAsync(retried, default, retryTime));
    }
}
