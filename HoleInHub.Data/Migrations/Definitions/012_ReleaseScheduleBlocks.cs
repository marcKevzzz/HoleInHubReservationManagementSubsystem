using MongoDB.Bson;
using MongoDB.Driver;

namespace HoleInHub.Data.Migrations.Definitions;

public sealed class Migration_012_ReleaseScheduleBlocks : IMongoMigration
{
    public string MigrationId => "012_ReleaseScheduleBlocks";
    public string Description => "Add optional, complete release audit to schedule blocks";
    public string CalculateChecksum() => MongoMigrationChecksum.Compute(MigrationId + ":v1:release-audit-all-or-none");

    public async Task UpAsync(IMongoDatabase database, CancellationToken ct)
    {
        using var collections = await database.ListCollectionsAsync(new ListCollectionsOptions
        { Filter = new BsonDocument("name", "schedule_blocks") }, ct);
        var collection = await collections.FirstAsync(ct);
        var validator = collection["options"]["validator"].AsBsonDocument.DeepClone().AsBsonDocument;
        var properties = validator["$jsonSchema"]["properties"].AsBsonDocument;
        properties["ReleasedUtc"] = new BsonDocument("bsonType", "date");
        properties["ReleasedBy"] = new BsonDocument { { "bsonType", "string" }, { "minLength", 1 } };
        properties["ReleaseReason"] = new BsonDocument { { "bsonType", "string" }, { "minLength", 1 }, { "maxLength", 500 } };
        validator["$or"] = new BsonArray
        {
            new BsonDocument
            {
                { "ReleasedUtc", new BsonDocument("$exists", false) },
                { "ReleasedBy", new BsonDocument("$exists", false) },
                { "ReleaseReason", new BsonDocument("$exists", false) }
            },
            new BsonDocument
            {
                { "ReleasedUtc", new BsonDocument("$exists", true) },
                { "ReleasedBy", new BsonDocument("$exists", true) },
                { "ReleaseReason", new BsonDocument("$exists", true) }
            }
        };
        if (await database.GetCollection<BsonDocument>("schedule_blocks")
            .Find(new BsonDocument("$nor", new BsonArray { validator })).Limit(1).AnyAsync(ct))
        {
            throw new InvalidOperationException("Existing schedule blocks contain incomplete or invalid release audit. Review them before retrying migration 012.");
        }
        await database.RunCommandAsync<BsonDocument>(new BsonDocument
        {
            { "collMod", "schedule_blocks" }, { "validator", validator },
            { "validationLevel", "strict" }, { "validationAction", "error" }
        }, cancellationToken: ct);
    }
}
