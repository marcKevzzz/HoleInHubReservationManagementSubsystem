using MongoDB.Bson;
using MongoDB.Driver;

namespace HoleInHub.Data.Migrations.Definitions;

public sealed class Migration_014_OperatingReviewClock : IMongoMigration
{
    public string MigrationId => "014_OperatingReviewClock";
    public string Description => "Optional saved review opening and alert instants for outside-hours submissions";
    public string CalculateChecksum() => MongoMigrationChecksum.Compute(MigrationId + ":v1:ReviewStartsUtc,ReviewAlertsUtc:optional-no-backfill");
    public async Task UpAsync(IMongoDatabase database, CancellationToken ct)
    {
        using var collections = await database.ListCollectionsAsync(new ListCollectionsOptions
        { Filter = new BsonDocument("name", "reservations") }, ct);
        var collection = await collections.FirstAsync(ct);
        var validator = collection["options"]["validator"].AsBsonDocument.DeepClone().AsBsonDocument;
        var properties = validator["$jsonSchema"]["properties"].AsBsonDocument;
        properties["ReviewStartsUtc"] = new BsonDocument("bsonType", "date");
        properties["ReviewAlertsUtc"] = new BsonDocument
        {
            { "bsonType", "array" }, { "minItems", 2 }, { "maxItems", 2 },
            { "items", new BsonDocument("bsonType", "date") }
        };
        await database.RunCommandAsync<BsonDocument>(new BsonDocument
        {
            { "collMod", "reservations" }, { "validator", validator },
            { "validationLevel", "strict" }, { "validationAction", "error" }
        }, cancellationToken: ct);
    }
}
