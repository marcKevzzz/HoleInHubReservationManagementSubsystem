using MongoDB.Bson;
using MongoDB.Driver;

namespace HoleInHub.Data.Migrations.Definitions;

public sealed class Migration_011_AdminScheduleBlocks : IMongoMigration
{
    public string MigrationId => "011_AdminScheduleBlocks";
    public string Description => "Validate optional block reason, creator, timestamp and retry hash while preserving legacy closures";
    public string CalculateChecksum() => MongoMigrationChecksum.Compute(MigrationId + ":v1:optional-audit-fields");

    public async Task UpAsync(IMongoDatabase database, CancellationToken ct)
    {
        using var collections = await database.ListCollectionsAsync(new ListCollectionsOptions
        { Filter = new BsonDocument("name", "schedule_blocks") }, ct);
        var collection = await collections.FirstAsync(ct);
        var validator = collection["options"]["validator"].AsBsonDocument.DeepClone().AsBsonDocument;
        var properties = validator["$jsonSchema"]["properties"].AsBsonDocument;
        properties["Reason"] = new BsonDocument { { "bsonType", "string" }, { "minLength", 1 }, { "maxLength", 500 } };
        properties["CreatedBy"] = new BsonDocument { { "bsonType", "string" }, { "minLength", 1 } };
        properties["CreatedUtc"] = new BsonDocument("bsonType", "date");
        properties["SubmissionHash"] = new BsonDocument { { "bsonType", "string" }, { "minLength", 64 }, { "maxLength", 64 } };
        if (await database.GetCollection<BsonDocument>("schedule_blocks")
            .Find(new BsonDocument("$nor", new BsonArray { validator })).Limit(1).AnyAsync(ct))
        {
            throw new InvalidOperationException("Existing schedule blocks contain incompatible audit fields. Review them before retrying migration 011.");
        }
        await database.RunCommandAsync<BsonDocument>(new BsonDocument
        {
            { "collMod", "schedule_blocks" }, { "validator", validator },
            { "validationLevel", "strict" }, { "validationAction", "error" }
        }, cancellationToken: ct);
    }
}
