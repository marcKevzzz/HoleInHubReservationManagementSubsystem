using MongoDB.Bson;
using MongoDB.Driver;

namespace HoleInHub.Data.Migrations.Definitions;

public sealed class Migration_013_ReservationAttendance : IMongoMigration
{
    public string MigrationId => "013_ReservationAttendance";
    public string Description => "Reservation revision and optional actual attendance with replay-safe audit events";
    public string CalculateChecksum() => MongoMigrationChecksum.Compute(MigrationId + ":v1:revision,attendance,command-audit");

    public async Task UpAsync(IMongoDatabase database, CancellationToken ct)
    {
        using var collections = await database.ListCollectionsAsync(new ListCollectionsOptions
        { Filter = new BsonDocument("name", "reservations") }, ct);
        var collection = await collections.FirstAsync(ct);
        var validator = collection["options"]["validator"].AsBsonDocument.DeepClone().AsBsonDocument;
        var properties = validator["$jsonSchema"]["properties"].AsBsonDocument;
        properties["Revision"] = new BsonDocument { { "bsonType", new BsonArray { "int", "long" } }, { "minimum", 0 } };
        properties["CheckedInUtc"] = new BsonDocument("bsonType", "date");
        properties["CheckedOutUtc"] = new BsonDocument("bsonType", "date");
        var history = properties["History"]["items"]["properties"].AsBsonDocument;
        foreach (var name in new[] { "ReservationCheckedIn", "ReservationCompleted" })
        {
            var events = history["EventType"]["enum"].AsBsonArray;
            if (!events.Contains(name)) { events.Add(name); }
        }
        history["CommandId"] = new BsonDocument { { "bsonType", "string" }, { "minLength", 32 }, { "maxLength", 32 } };
        history["CommandHash"] = new BsonDocument { { "bsonType", "string" }, { "minLength", 64 }, { "maxLength", 64 } };
        history["PreviousReservationStatus"] = properties["Status"].DeepClone();
        var records = database.GetCollection<BsonDocument>("reservations");
        if (await records.Find(new BsonDocument("$nor", new BsonArray { validator })).Limit(1).AnyAsync(ct))
        {
            throw new InvalidOperationException("Reservations contain invalid attendance/revision values. Review before retrying migration 013.");
        }
        await records.UpdateManyAsync(new BsonDocument("Revision", new BsonDocument("$exists", false)),
            new BsonDocument("$set", new BsonDocument("Revision", 0L)), cancellationToken: ct);
        await database.RunCommandAsync<BsonDocument>(new BsonDocument
        {
            { "collMod", "reservations" }, { "validator", validator },
            { "validationLevel", "strict" }, { "validationAction", "error" }
        }, cancellationToken: ct);
    }
}
