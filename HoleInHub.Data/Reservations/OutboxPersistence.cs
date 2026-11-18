using HoleInHub.Data.Context;
using MongoDB.Driver;
namespace HoleInHub.Data.Reservations;

public sealed partial class OutboxPersistence(IMongoDbContext context)
{
    private IMongoCollection<OutboxRecord> Messages => context.Database.GetCollection<OutboxRecord>("outbox");
    public async Task EnqueueAsync(OutboxRecord message, CancellationToken ct) { try { await Messages.InsertOneAsync(message, cancellationToken: ct); } catch (MongoWriteException e) when (e.WriteError.Category == ServerErrorCategory.DuplicateKey) { } }
    public async Task<OutboxRecord?> ClaimAsync(DateTime now, CancellationToken ct)
    {
        // A worker can crash on its last attempt before recording the terminal failure.
        await Messages.UpdateManyAsync(x => x.SentUtc == null && x.SuppressedUtc == null && !x.Failed && x.Attempts >= 5 && (x.LeaseUntilUtc == null || x.LeaseUntilUtc <= now), Builders<OutboxRecord>.Update.Set(x => x.Failed, true).Set(x => x.FailedUtc, now).Set(x => x.FailureType, "AttemptsExhausted").Set(x => x.LeaseUntilUtc, null), cancellationToken: ct);
        return await Messages.FindOneAndUpdateAsync<OutboxRecord>(x => x.SentUtc == null && x.SuppressedUtc == null && !x.Failed && x.Attempts < 5 && x.DueUtc <= now && (x.LeaseUntilUtc == null || x.LeaseUntilUtc <= now), Builders<OutboxRecord>.Update.Set(x => x.LeaseUntilUtc, now.AddMinutes(2)).Set(x => x.LastAttemptUtc, now).Set(x => x.LeaseToken, Guid.NewGuid().ToString("N")).Inc(x => x.Attempts, 1), new FindOneAndUpdateOptions<OutboxRecord> { ReturnDocument = ReturnDocument.After }, ct);
    }
    public Task CompleteAsync(string id, string leaseToken, CancellationToken ct) => Messages.UpdateOneAsync(x => x.Id == id && x.LeaseToken == leaseToken && !x.Failed && x.SuppressedUtc == null && x.SentUtc == null, Builders<OutboxRecord>.Update.Set(x => x.SentUtc, DateTime.UtcNow).Set(x => x.LeaseUntilUtc, null), cancellationToken: ct);
    public Task SuppressAsync(string id, string leaseToken, DateTime now, CancellationToken ct) => Messages.UpdateOneAsync(x => x.Id == id && x.LeaseToken == leaseToken && x.SentUtc == null && x.SuppressedUtc == null, Builders<OutboxRecord>.Update.Set(x => x.SuppressedUtc, now).Set(x => x.LeaseUntilUtc, null), cancellationToken: ct);
    public async Task RetryAsync(string id, string leaseToken, DateTime now, CancellationToken ct, string failureType = "DeliveryFailure")
    {
        var failure = failureType is "Timeout" or "ContentUnavailable" ? failureType : "DeliveryFailure";
        await Messages.UpdateOneAsync(x => x.Id == id && x.LeaseToken == leaseToken && x.Attempts >= 5 && x.SentUtc == null && x.SuppressedUtc == null, Builders<OutboxRecord>.Update.Set(x => x.Failed, true).Set(x => x.FailedUtc, now).Set(x => x.FailureType, failure).Set(x => x.LeaseUntilUtc, null), cancellationToken: ct);
        await Messages.UpdateOneAsync(x => x.Id == id && x.LeaseToken == leaseToken && !x.Failed && x.SentUtc == null && x.SuppressedUtc == null, Builders<OutboxRecord>.Update.Set(x => x.FailureType, failure).Set(x => x.DueUtc, now.AddMinutes(2)).Set(x => x.LeaseUntilUtc, null), cancellationToken: ct);
    }
    public async Task<bool> ReplayFailedAsync(string id, DateTime now, CancellationToken ct)
    {
        var result = await Messages.UpdateOneAsync(x => x.Id == id && x.Failed && x.SentUtc == null && x.SuppressedUtc == null && (x.LeaseUntilUtc == null || x.LeaseUntilUtc <= now) && (x.ExpiresUtc == null || x.ExpiresUtc > now) && (!x.Body.StartsWith("protected:") || x.ExpiresUtc != null), Builders<OutboxRecord>.Update.Set(x => x.Failed, false).Set(x => x.Attempts, 0).Set(x => x.DueUtc, now).Set(x => x.LeaseUntilUtc, null).Set(x => x.LeaseToken, null).Set(x => x.FailedUtc, null).Set(x => x.FailureType, null), cancellationToken: ct);
        return result.ModifiedCount == 1;
    }
    public async Task<bool> IsEligibleAsync(OutboxRecord message, CancellationToken ct, DateTime? nowUtc = null)
    {
        var now = nowUtc ?? DateTime.UtcNow;
        if (message.ExpiresUtc <= now) { return false; }
        if (message.VerificationId is not null)
        {
            var proof = await context.Database.GetCollection<GuestVerificationRecord>("guest_verifications").Find(x => x.Id == message.VerificationId).FirstOrDefaultAsync(ct);
            if (proof is null || proof.Email != message.Recipient || proof.SentUtc != message.VerificationIssuedUtc || proof.ExpiresUtc <= now || proof.VerifiedUtc != null || proof.ConsumedBySubmissionKey != null) { return false; }
        }
        if (message.ReservationId is null) { return true; }
        var reservation = await context.Database.GetCollection<ReservationRecord>("reservations")
            .Find(x => x.Id == message.ReservationId).FirstOrDefaultAsync(ct);
        if (reservation is null || (message.RequiredStatus is not null && reservation.Status != message.RequiredStatus)) { return false; }
        if (message.ReminderStartUtc is { } reminderStart)
        {
            return reservation.Status == "Confirmed" && reservation.CheckedInUtc == null && !reservation.CancellationRequested
                && reservation.StartUtc == reminderStart && reservation.Email == message.Recipient
                && now < reminderStart && reminderStart <= now.AddMinutes(15);
        }
        if (message.RequiredStatus != "Pending") { return true; }

        return message.Id.StartsWith($"{reservation.Id}:overdue:", StringComparison.Ordinal)
            ? reservation.Service != "WholeShop" && reservation.ReviewDeadlineUtc <= now && reservation.StartUtc > now
            : reservation.ReviewDeadlineUtc > now;
    }
}
