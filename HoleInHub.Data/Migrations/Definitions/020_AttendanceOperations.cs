using MongoDB.Bson;
using MongoDB.Driver;

namespace HoleInHub.Data.Migrations.Definitions;

public sealed class Migration_020_AttendanceOperations : IMongoMigration
{
    public string MigrationId => "020_AttendanceOperations";
    public string Description => "No-show, corrected attendance and session extension audit snapshots";
    public string CalculateChecksum() => MongoMigrationChecksum.Compute(MigrationId + ":v1:attendance-before-after:no-show:correct:extend");

    public async Task UpAsync(IMongoDatabase database, CancellationToken ct)
    {
        using var collections = await database.ListCollectionsAsync(new ListCollectionsOptions
        { Filter = new BsonDocument("name", "reservations") }, ct);
        var collection = await collections.FirstAsync(ct);
        var validator = collection["options"]["validator"].AsBsonDocument.DeepClone().AsBsonDocument;
        var properties = validator["$jsonSchema"]["properties"].AsBsonDocument;
        var history = properties["History"]["items"]["properties"].AsBsonDocument;
        var snapshot = new BsonDocument
        {
            { "bsonType", "object" },
            { "required", new BsonArray { "Status", "StartUtc", "EndUtc" } },
            { "properties", new BsonDocument
                {
                    { "Status", properties["Status"].DeepClone() },
                    { "StartUtc", new BsonDocument("bsonType", "date") },
                    { "EndUtc", new BsonDocument("bsonType", "date") },
                    { "CheckedInUtc", new BsonDocument("bsonType", "date") },
                    { "CheckedOutUtc", new BsonDocument("bsonType", "date") }
                }
            }
        };
        history["AttendanceBefore"] = snapshot;
        history["AttendanceAfter"] = snapshot.DeepClone();
        var events = history["EventType"]["enum"].AsBsonArray;
        foreach (var name in new[] { "ReservationNoShow", "AttendanceCorrected", "ReservationExtended" })
        {
            if (!events.Contains(name)) { events.Add(name); }
        }
        await database.RunCommandAsync<BsonDocument>(new BsonDocument
        {
            { "collMod", "reservations" }, { "validator", validator },
            { "validationLevel", "strict" }, { "validationAction", "error" }
        }, cancellationToken: ct);
        await database.GetCollection<BsonDocument>("allocation_guards").UpdateOneAsync(
            new BsonDocument("_id", "attendance-operations"), new BsonDocument("$setOnInsert", new BsonDocument("version", 0L)),
            new UpdateOptions { IsUpsert = true }, ct);
    }
}
