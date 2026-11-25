using MongoDB.Bson;
using MongoDB.Driver;

namespace HoleInHub.Data.Reservations;

public sealed record CustomerNotificationRow(string BookingId, int Index, string Reference, string EventType, DateTime AtUtc, bool Unread);
public sealed record CustomerNotificationRead(string BookingId, int Index, DateTime AtUtc);

public sealed partial class ReservationPersistence
{
    private static BsonDocument[] CustomerNotificationStages(string accountId, string[] types) =>
    [
        new("$match", new BsonDocument("AccountId", accountId)),
        new("$unwind", new BsonDocument { { "path", "$History" }, { "includeArrayIndex", "Index" } }),
        new("$match", new BsonDocument("History.EventType", new BsonDocument("$in", new BsonArray(types)))),
        new("$match", new BsonDocument("$expr", new BsonDocument("$not", new BsonArray { new BsonDocument("$in", new BsonArray
            { "$Index", new BsonDocument("$ifNull", new BsonArray { "$CustomerDismissedHistory", new BsonArray() }) }) }))),
        new("$project", new BsonDocument
        {
            { "Index", 1 }, { "Reference", 1 }, { "EventType", "$History.EventType" }, { "AtUtc", "$History.AtUtc" },
            { "Unread", new BsonDocument("$not", new BsonArray { new BsonDocument("$in", new BsonArray
                { "$Index", new BsonDocument("$ifNull", new BsonArray { "$CustomerReadHistory", new BsonArray() }) }) }) }
        })
    ];

    public Task<bool> HasUnreadCustomerNotificationsAsync(string accountId, string[] types, CancellationToken ct)
    {
        BsonDocument[] stages = [
            .. CustomerNotificationStages(accountId, types),
            new("$match", new BsonDocument("Unread", true)),
            new("$limit", 1)
        ];
        PipelineDefinition<ReservationRecord, BsonDocument> pipeline = stages;
        return Reservations.Aggregate(pipeline).AnyAsync(ct);
    }

    public async Task<CustomerNotificationRow[]> ReadCustomerNotificationsAsync(string accountId, string[] types, int skip, int take, CancellationToken ct, bool unreadOnly = false)
    {
        BsonDocument[] stages = [
            .. CustomerNotificationStages(accountId, types),
            new("$match", unreadOnly ? new BsonDocument("Unread", true) : new BsonDocument()),
            new("$sort", new BsonDocument { { "AtUtc", -1 }, { "_id", 1 }, { "Index", -1 } }),
            new("$skip", skip), new("$limit", take)
        ];
        PipelineDefinition<ReservationRecord, BsonDocument> pipeline = stages;
        var rows = await Reservations.Aggregate(pipeline).ToListAsync(ct);
        return rows.Select(row => new CustomerNotificationRow(row["_id"].AsString, row["Index"].ToInt32(), row["Reference"].AsString,
            row["EventType"].AsString, row["AtUtc"].ToUniversalTime(), row["Unread"].AsBoolean)).ToArray();
    }

    public Task MarkCustomerNotificationsReadAsync(string accountId, string[] types, IReadOnlyList<CustomerNotificationRead> rows, CancellationToken ct)
        => ChangeCustomerNotificationsAsync(accountId, types, rows, nameof(ReservationRecord.CustomerReadHistory), ct);

    public Task DismissCustomerNotificationsAsync(string accountId, string[] types, IReadOnlyList<CustomerNotificationRead> rows, CancellationToken ct)
        => ChangeCustomerNotificationsAsync(accountId, types, rows, nameof(ReservationRecord.CustomerDismissedHistory), ct);

    private Task ChangeCustomerNotificationsAsync(string accountId, string[] types, IReadOnlyList<CustomerNotificationRead> rows, string field, CancellationToken ct)
    {
        var updates = rows.Select(row => new UpdateOneModel<ReservationRecord>(
            Builders<ReservationRecord>.Filter.Where(x => x.Id == row.BookingId && x.AccountId == accountId) &
            new BsonDocument
            {
                { $"History.{row.Index}.AtUtc", new BsonDateTime(row.AtUtc) },
                { $"History.{row.Index}.EventType", new BsonDocument("$in", new BsonArray(types)) }
            },
            Builders<ReservationRecord>.Update.AddToSet<int>(field, row.Index)));
        return Reservations.BulkWriteAsync(updates, new BulkWriteOptions { IsOrdered = false }, ct);
    }

    public Task MarkAllCustomerNotificationsReadAsync(string accountId, string[] types, DateTime throughUtc, CancellationToken ct)
    {
        var indices = new BsonDocument("$filter", new BsonDocument
        {
            { "input", new BsonDocument("$range", new BsonArray { 0, new BsonDocument("$size", new BsonDocument("$ifNull", new BsonArray { "$History", new BsonArray() })) }) },
            { "as", "i" },
            { "cond", new BsonDocument("$let", new BsonDocument
                {
                    { "vars", new BsonDocument("h", new BsonDocument("$arrayElemAt", new BsonArray { "$History", "$$i" })) },
                    { "in", new BsonDocument("$and", new BsonArray
                        {
                            new BsonDocument("$in", new BsonArray { "$$h.EventType", new BsonArray(types) }),
                            new BsonDocument("$lte", new BsonArray { "$$h.AtUtc", new BsonDateTime(throughUtc) })
                        }) }
                }) }
        });
        var update = new PipelineUpdateDefinition<ReservationRecord>(new[] { new BsonDocument("$set", new BsonDocument("CustomerReadHistory",
            new BsonDocument("$setUnion", new BsonArray { new BsonDocument("$ifNull", new BsonArray { "$CustomerReadHistory", new BsonArray() }), indices }))) });
        return Reservations.UpdateManyAsync(x => x.AccountId == accountId, update, cancellationToken: ct);
    }
}
