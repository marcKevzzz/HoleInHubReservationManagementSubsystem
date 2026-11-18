using MongoDB.Bson;
using MongoDB.Driver;

namespace HoleInHub.Data.Reservations;

public sealed record NotificationDeliveryRow(string Id, string Recipient, string Subject, DateTime DueUtc, DateTime? LeaseUntilUtc,
    DateTime? SentUtc, DateTime? SuppressedUtc, DateTime? FailedUtc, DateTime? LastAttemptUtc, string? FailureType, int Attempts, bool Failed,
    DateTime? ExpiresUtc, bool ProtectedWithoutExpiry, Dictionary<string, DateTime>? ReadThroughByAdmin, string? ReservationId = null);

public sealed partial class OutboxPersistence
{
    private static BsonDocument LatestDeliveryActivity() =>
        new("$max", new BsonArray { "$DueUtc", "$LastAttemptUtc", "$SentUtc", "$FailedUtc", "$SuppressedUtc" });

    public Task<long> CountUnreadDeliveryAsync(string adminKey, DateTime sinceUtc, CancellationToken ct, bool operational = false, DateTime? now = null)
    {
        var dueBefore = now ?? DateTime.UtcNow;
        var readThrough = new BsonDocument("$max", new BsonArray { new BsonDateTime(sinceUtc), "$ReadThroughByAdmin." + adminKey });
        var activity = operational ? (BsonValue)new BsonString("$DueUtc") : LatestDeliveryActivity();
        FilterDefinition<OutboxRecord> unread = new BsonDocument("$expr", new BsonDocument("$gt", new BsonArray { activity, readThrough }));
        if (operational) { unread &= Builders<OutboxRecord>.Filter.Where(x => x.ReservationId != null && x.DueUtc <= dueBefore && x.SuppressedUtc == null); }
        return Messages.CountDocumentsAsync(unread, new CountOptions { Limit = 100 }, ct);
    }

    public Task MarkDeliveryRowsReadAsync(string adminKey, IReadOnlyDictionary<string, DateTime> rows, CancellationToken ct, bool operational = false)
    {
        var updates = rows.Select(row => new UpdateOneModel<OutboxRecord>(
            Builders<OutboxRecord>.Filter.Eq(x => x.Id, row.Key) &
            (operational ? Builders<OutboxRecord>.Filter.Ne(x => x.ReservationId, null) : Builders<OutboxRecord>.Filter.Empty) &
            new BsonDocument("$expr", new BsonDocument("$eq", new BsonArray { operational ? (BsonValue)new BsonString("$DueUtc") : LatestDeliveryActivity(), new BsonDateTime(row.Value) })),
            Builders<OutboxRecord>.Update.Max("ReadThroughByAdmin." + adminKey, row.Value)));
        return Messages.BulkWriteAsync(updates, new BulkWriteOptions { IsOrdered = false }, ct);
    }

    public async Task<IReadOnlyList<NotificationDeliveryRow>> ListDeliveryAsync(string status, int skip, int take, DateTime now, CancellationToken ct)
    {
        var filter = Builders<OutboxRecord>.Filter;
        var pending = filter.Where(x => x.SentUtc == null && x.SuppressedUtc == null && !x.Failed);
        var selected = status switch
        {
            "staff" => filter.Where(x => x.ReservationId != null && x.DueUtc <= now && x.SuppressedUtc == null),
            "queued" => pending & filter.Where(x => x.LeaseUntilUtc == null || x.LeaseUntilUtc <= now),
            "sending" => pending & filter.Where(x => x.LeaseUntilUtc > now),
            "sent" => filter.Where(x => x.SentUtc != null),
            "failed" => filter.Where(x => x.Failed && x.SentUtc == null && x.SuppressedUtc == null),
            "suppressed" => filter.Where(x => x.SuppressedUtc != null),
            _ => filter.Empty
        };
        return await Messages.Find(selected).SortByDescending(x => x.DueUtc).ThenByDescending(x => x.Id).Skip(skip).Limit(take)
            .Project(x => new NotificationDeliveryRow(x.Id, x.Recipient, x.Subject, x.DueUtc, x.LeaseUntilUtc, x.SentUtc,
                x.SuppressedUtc, x.FailedUtc, x.LastAttemptUtc, x.FailureType, x.Attempts, x.Failed, x.ExpiresUtc,
                x.ExpiresUtc == null && x.Body.StartsWith("protected:"), x.ReadThroughByAdmin, x.ReservationId))
            .ToListAsync(ct);
    }

    public async Task<bool> RetryDeliveryAsync(string id, DateTime now, CancellationToken ct)
    {
        var failed = await Messages.Find(x => x.Id == id && x.Failed && x.SentUtc == null && x.SuppressedUtc == null)
            .FirstOrDefaultAsync(ct);
        if (failed is null || !await IsEligibleAsync(failed, ct, now)) { return false; }
        return await ReplayFailedAsync(id, now, ct);
    }
}
