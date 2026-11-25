using MongoDB.Bson;
using MongoDB.Driver;

namespace HoleInHub.Data.Migrations.Definitions;

public sealed class Migration_027_CoachDirectory : IMongoMigration
{
    public string MigrationId => "027_CoachDirectory";
    public string Description => "Index coach profiles and named lesson assignments";
    public string CalculateChecksum() => MongoMigrationChecksum.Compute(MigrationId + ":coach-id:assignment-range:history:v2");

    public async Task UpAsync(IMongoDatabase database, CancellationToken ct)
    {
        using var cursor = await database.ListCollectionsAsync(new ListCollectionsOptions { Filter = new BsonDocument("name", "reservations") }, ct);
        var validator = (await cursor.FirstAsync(ct))["options"]["validator"].AsBsonDocument;
        var fields = validator["$jsonSchema"]["properties"].AsBsonDocument;
        fields["coach_id"] = new BsonDocument("bsonType", "string");
        fields["CoachName"] = new BsonDocument("bsonType", "string");
        var events = fields["History"]["items"]["properties"]["EventType"]["enum"].AsBsonArray;
        if (!events.Contains("CoachAssigned")) { events.Add("CoachAssigned"); }
        await database.RunCommandAsync<BsonDocument>(new BsonDocument { { "collMod", "reservations" }, { "validator", validator } }, cancellationToken: ct);
        var keys = Builders<BsonDocument>.IndexKeys;
        await database.GetCollection<BsonDocument>("coaches").Indexes.CreateOneAsync(new CreateIndexModel<BsonDocument>(
            keys.Ascending("coach_id"), new CreateIndexOptions { Unique = true, Name = "ux_coach_id" }), cancellationToken: ct);
        await database.GetCollection<BsonDocument>("reservations").Indexes.CreateOneAsync(new CreateIndexModel<BsonDocument>(
            keys.Ascending("Service").Ascending("Status").Ascending("StartUtc").Ascending("EndUtc").Ascending("coach_id"),
            new CreateIndexOptions { Name = "ix_coach_assignment_range" }), cancellationToken: ct);
    }
}
