using MongoDB.Bson;
using MongoDB.Driver;

namespace HoleInHub.Data.Migrations.Definitions;

public sealed class Migration_024_OutboxIdentifier : IMongoMigration
{
    public string MigrationId => "024_OutboxIdentifier";
    public string Description => "Assign unique OUT identifiers to notification messages";
    public string CalculateChecksum() => MongoMigrationChecksum.Compute(MigrationId + ":v1:OUT:sha256-first16");

    public async Task UpAsync(IMongoDatabase database, CancellationToken ct)
    {
        const string field = "outbox_id";
        var messages = database.GetCollection<BsonDocument>("outbox");
        await messages.Indexes.CreateOneAsync(new CreateIndexModel<BsonDocument>(
            Builders<BsonDocument>.IndexKeys.Ascending(field),
            new CreateIndexOptions { Name = "ux_outbox_outbox_id", Unique = true, Sparse = true }), cancellationToken: ct);

        using var metadata = await database.ListCollectionsAsync(new ListCollectionsOptions { Filter = new BsonDocument("name", "outbox") }, ct);
        var validator = (await metadata.FirstAsync(ct))["options"]["validator"].AsBsonDocument;
        var schema = validator["$jsonSchema"].AsBsonDocument;
        var required = schema["required"].AsBsonArray;
        if (!required.Contains(field)) { required.Add(field); }
        schema["properties"][field] = new BsonDocument { { "bsonType", "string" }, { "pattern", @"^OUT-[0-9A-F]{16}\z" } };
        await database.RunCommandAsync<BsonDocument>(new BsonDocument { { "collMod", "outbox" }, { "validator", validator } }, cancellationToken: ct);

        var missing = Builders<BsonDocument>.Filter.Exists(field, false);
        using var cursor = await messages.Find(missing).SortBy(row => row["_id"]).Project(new BsonDocument("_id", 1)).ToCursorAsync(ct);
        while (await cursor.MoveNextAsync(ct))
        {
            var updates = cursor.Current.Select(row => new UpdateOneModel<BsonDocument>(
                Builders<BsonDocument>.Filter.Eq("_id", row["_id"]) & missing,
                Builders<BsonDocument>.Update.Set(field, BusinessIdentifier.Format("OUT", row["_id"].AsString)))).ToList();
            if (updates.Count > 0) { await messages.BulkWriteAsync(updates, cancellationToken: ct); }
        }
    }
}
