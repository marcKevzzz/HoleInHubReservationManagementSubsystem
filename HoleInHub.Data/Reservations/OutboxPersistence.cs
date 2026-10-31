using HoleInHub.Data.Context;
using MongoDB.Driver;
namespace HoleInHub.Data.Reservations;

public sealed class OutboxPersistence(IMongoDbContext context)
{
    private IMongoCollection<OutboxRecord> Messages => context.Database.GetCollection<OutboxRecord>("outbox");
    public async Task EnqueueAsync(OutboxRecord message, CancellationToken ct) { try { await Messages.InsertOneAsync(message, cancellationToken: ct); } catch (MongoWriteException e) when (e.WriteError.Category == ServerErrorCategory.DuplicateKey) { } }
    public async Task<OutboxRecord?> ClaimAsync(DateTime now, CancellationToken ct)
    {
        // A worker can crash on its last attempt before recording the terminal failure.
        await Messages.UpdateManyAsync(x => x.SentUtc == null && !x.Failed && x.Attempts >= 5 && (x.LeaseUntilUtc == null || x.LeaseUntilUtc <= now), Builders<OutboxRecord>.Update.Set(x => x.Failed, true).Set(x => x.LeaseUntilUtc, null), cancellationToken: ct);
        return await Messages.FindOneAndUpdateAsync<OutboxRecord>(x => x.SentUtc == null && !x.Failed && x.Attempts < 5 && x.DueUtc <= now && (x.LeaseUntilUtc == null || x.LeaseUntilUtc <= now), Builders<OutboxRecord>.Update.Set(x => x.LeaseUntilUtc, now.AddMinutes(2)).Set(x => x.LeaseToken, Guid.NewGuid().ToString("N")).Inc(x => x.Attempts, 1), new FindOneAndUpdateOptions<OutboxRecord> { ReturnDocument = ReturnDocument.After }, ct);
    }
    public Task CompleteAsync(string id, string leaseToken, CancellationToken ct) => Messages.UpdateOneAsync(x => x.Id == id && x.LeaseToken == leaseToken && !x.Failed, Builders<OutboxRecord>.Update.Set(x => x.SentUtc, DateTime.UtcNow).Set(x => x.LeaseUntilUtc, null), cancellationToken: ct);
    public async Task RetryAsync(string id, string leaseToken, DateTime now, CancellationToken ct)
    {
        await Messages.UpdateOneAsync(x => x.Id == id && x.LeaseToken == leaseToken && x.Attempts >= 5, Builders<OutboxRecord>.Update.Set(x => x.Failed, true).Set(x => x.LeaseUntilUtc, null), cancellationToken: ct);
        await Messages.UpdateOneAsync(x => x.Id == id && x.LeaseToken == leaseToken && !x.Failed, Builders<OutboxRecord>.Update.Set(x => x.DueUtc, now.AddMinutes(2)).Set(x => x.LeaseUntilUtc, null), cancellationToken: ct);
    }
    public async Task<bool> ReplayFailedAsync(string id, DateTime now, CancellationToken ct)
    {
        var result = await Messages.UpdateOneAsync(x => x.Id == id && x.Failed && x.SentUtc == null, Builders<OutboxRecord>.Update.Set(x => x.Failed, false).Set(x => x.Attempts, 0).Set(x => x.DueUtc, now).Set(x => x.LeaseUntilUtc, null).Set(x => x.LeaseToken, null), cancellationToken: ct);
        return result.ModifiedCount == 1;
    }
    public async Task<bool> IsEligibleAsync(OutboxRecord message, CancellationToken ct) { if (message.ReservationId == null) { return true; } var reservation = await context.Database.GetCollection<ReservationRecord>("reservations").Find(x => x.Id == message.ReservationId).FirstOrDefaultAsync(ct); return reservation != null && reservation.Status == message.RequiredStatus && (message.RequiredStatus != "Pending" || reservation.ReviewDeadlineUtc > DateTime.UtcNow); }
}
