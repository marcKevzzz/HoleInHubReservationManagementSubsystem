using MongoDB.Bson;
using MongoDB.Driver;

namespace HoleInHub.Data.Migrations.Definitions;

public sealed class Migration_010_StaffAssistedReservations : IMongoMigration
{
    public string MigrationId => "010_StaffAssistedReservations";
    public string Description => "Allow phone-only assisted reservations without assigning a customer account";
    public string CalculateChecksum() => MongoMigrationChecksum.Compute(MigrationId + ":v1:staff-scope,phone-only,booking-source");

    public async Task UpAsync(IMongoDatabase database, CancellationToken ct)
    {
        using var collections = await database.ListCollectionsAsync(new ListCollectionsOptions
        { Filter = new BsonDocument("name", "reservations") }, ct);
        var collection = await collections.FirstAsync(ct);
        var validator = collection["options"]["validator"].AsBsonDocument.DeepClone().AsBsonDocument;
        var properties = validator["$jsonSchema"]["properties"].AsBsonDocument;
        properties["Email"]["minLength"] = 0;
        properties["BookingSource"] = new BsonDocument("enum", new BsonArray { "Walk-in", "Phone", "Messaging" });
        validator["$or"] = new BsonArray
        {
            new BsonDocument("Email", new BsonDocument("$ne", "")),
            new BsonDocument
            {
                { "AccountId", new BsonRegularExpression("^staff:.+") },
                { "CustomerAccountId", new BsonDocument("$exists", false) },
                { "BookingSource", new BsonDocument("$exists", true) },
                { "Phone", new BsonRegularExpression("^[0-9]{7,15}$") }
            }
        };
        await database.RunCommandAsync<BsonDocument>(new BsonDocument
        {
            { "collMod", "reservations" }, { "validator", validator },
            { "validationLevel", "strict" }, { "validationAction", "error" }
        }, cancellationToken: ct);
    }
}
