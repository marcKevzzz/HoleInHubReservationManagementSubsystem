using MongoDB.Bson;
using MongoDB.Driver;

namespace HoleInHub.Data.Migrations.Definitions;

public sealed class Migration_015_ScheduleChanges : IMongoMigration
{
    public string MigrationId => "015_ScheduleChanges";
    public string Description => "Bounded schedule proposals and audited scheduling decisions";
    public string CalculateChecksum() => MongoMigrationChecksum.Compute(MigrationId + ":v1:ScheduleChange:requested-approved-declined");
    public async Task UpAsync(IMongoDatabase database, CancellationToken ct)
    {
        using var collections = await database.ListCollectionsAsync(new ListCollectionsOptions { Filter = new BsonDocument("name", "reservations") }, ct);
        var collection = await collections.FirstAsync(ct);
        var validator = collection["options"]["validator"].AsBsonDocument.DeepClone().AsBsonDocument;
        var properties = validator["$jsonSchema"]["properties"].AsBsonDocument;
        var proposal = new BsonDocument
        {
            { "bsonType", "object" },
            { "required", new BsonArray { "_id", "PayloadHash", "BaseRevision", "Status", "RequestedUtc", "OriginalStartUtc", "OriginalEndUtc", "OriginalResourceIds", "StartUtc", "EndUtc", "ResourceIds", "ResourceNames", "Reason" } },
            { "properties", new BsonDocument
                {
                    { "_id", Text(32, 32) }, { "PayloadHash", Text(64, 64) },
                    { "BaseRevision", new BsonDocument { { "bsonType", new BsonArray { "int", "long" } }, { "minimum", 0 } } },
                    { "Status", new BsonDocument("enum", new BsonArray { "Pending", "Approved", "Declined" }) },
                    { "RequestedUtc", new BsonDocument("bsonType", "date") },
                    { "DecidedUtc", new BsonDocument("bsonType", new BsonArray { "date", "null" }) },
                    { "OriginalStartUtc", new BsonDocument("bsonType", "date") },
                    { "OriginalEndUtc", new BsonDocument("bsonType", "date") },
                    { "StartUtc", new BsonDocument("bsonType", "date") }, { "EndUtc", new BsonDocument("bsonType", "date") },
                    { "OriginalResourceIds", Ids() }, { "ResourceIds", Ids() },
                    { "ResourceNames", new BsonDocument("bsonType", "object") }, { "Reason", Text(1, 500) }
                }
            }
        };
        properties["ScheduleChange"] = proposal;
        var history = properties["History"]["items"]["properties"].AsBsonDocument;
        history["ScheduleChange"] = proposal.DeepClone();
        foreach (var name in new[] { "ScheduleChangeRequested", "ScheduleChangeApproved", "ScheduleChangeDeclined" })
        {
            var events = history["EventType"]["enum"].AsBsonArray;
            if (!events.Contains(name)) { events.Add(name); }
        }
        await database.RunCommandAsync<BsonDocument>(new BsonDocument
        {
            { "collMod", "reservations" }, { "validator", validator },
            { "validationLevel", "strict" }, { "validationAction", "error" }
        }, cancellationToken: ct);
    }
    private static BsonDocument Text(int min, int max) => new() { { "bsonType", "string" }, { "minLength", min }, { "maxLength", max } };
    private static BsonDocument Ids() => new() { { "bsonType", "array" }, { "minItems", 1 }, { "maxItems", 8 }, { "uniqueItems", true }, { "items", Text(1, 100) } };
}
