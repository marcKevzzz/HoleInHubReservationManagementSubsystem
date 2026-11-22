using MongoDB.Bson;
using MongoDB.Driver;

namespace HoleInHub.Data.Migrations.Definitions;

public sealed class Migration_023_SettlementCheckout : IMongoMigration
{
    public string MigrationId => "023_SettlementCheckout";
    public string Description => "Support separate session departure and verified financial checkout";
    public string CalculateChecksum() => MongoMigrationChecksum.Compute(MigrationId + ":v1");

    public async Task UpAsync(IMongoDatabase database, CancellationToken ct)
    {
        using var cursor = await database.ListCollectionsAsync(new ListCollectionsOptions { Filter = new BsonDocument("name", "reservations") }, ct);
        var validator = (await cursor.FirstAsync(ct))["options"]["validator"].AsBsonDocument;
        var fields = validator["$jsonSchema"]["properties"].AsBsonDocument;
        var history = fields["History"]["items"]["properties"].AsBsonDocument;
        foreach (var field in new[] { fields["Status"], history["ReservationStatus"], history["PreviousReservationStatus"] })
        {
            var states = field["enum"].AsBsonArray;
            if (!states.Contains("Awaiting settlement")) { states.Add("Awaiting settlement"); }
        }
        foreach (var name in new[] { "SessionEnded", "FinancialCheckoutCompleted" })
        {
            var events = history["EventType"]["enum"].AsBsonArray;
            if (!events.Contains(name)) { events.Add(name); }
        }
        fields["CheckoutCompletedUtc"] = new BsonDocument("bsonType", "date");
        fields["FinalSettlement"] = BsonDocument.Parse("""
            { "bsonType": "object", "required": ["ReservationId","BillReference","VerifiedUtc"],
              "properties": { "ReservationId": { "bsonType": "string","minLength": 1 },
                              "BillReference": { "bsonType": "string","minLength": 1 },
                              "VerifiedUtc": { "bsonType": "date" } } }
            """);
        await database.RunCommandAsync<BsonDocument>(new BsonDocument { { "collMod", "reservations" }, { "validator", validator } }, cancellationToken: ct);
    }
}
