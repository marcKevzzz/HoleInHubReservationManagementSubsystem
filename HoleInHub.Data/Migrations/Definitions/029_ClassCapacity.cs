using MongoDB.Bson;
using MongoDB.Driver;

namespace HoleInHub.Data.Migrations.Definitions;

public sealed class Migration_029_ClassCapacity : IMongoMigration
{
    public string MigrationId => "029_ClassCapacity";
    public string Description => "Allow ten learners per scheduled class";
    public string CalculateChecksum() => MongoMigrationChecksum.Compute(MigrationId + ":v1:capacity-heldseats-10");
    public async Task UpAsync(IMongoDatabase database, CancellationToken ct)
    {
        using var collections = await database.ListCollectionsAsync(new ListCollectionsOptions
        { Filter = new BsonDocument("name", "class_sessions") }, ct);
        var validator = (await collections.SingleAsync(ct))["options"]["validator"].AsBsonDocument;
        var fields = validator["$jsonSchema"]["properties"].AsBsonDocument;
        fields["Capacity"]["maximum"] = 10;
        fields["HeldSeats"]["maximum"] = 10;
        await database.RunCommandAsync<BsonDocument>(new BsonDocument
        { { "collMod", "class_sessions" }, { "validator", validator } }, cancellationToken: ct);
    }
}
