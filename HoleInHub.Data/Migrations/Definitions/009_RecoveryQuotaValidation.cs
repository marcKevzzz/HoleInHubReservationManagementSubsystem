using MongoDB.Bson;
using MongoDB.Driver;

namespace HoleInHub.Data.Migrations.Definitions;

public sealed class Migration_009_RecoveryQuotaValidation : IMongoMigration
{
    public string MigrationId => "009_RecoveryQuotaValidation";
    public string Description => "Validate recovery quotas even when the collection predates migration 007";
    public string CalculateChecksum() => MongoMigrationChecksum.Compute(MigrationId + ":v1:" + Schema().ToJson());

    public async Task UpAsync(IMongoDatabase database, CancellationToken ct)
    {
        var validator = new BsonDocument("$jsonSchema", Schema());
        var invalid = new BsonDocument("$nor", new BsonArray { validator });
        if (await database.GetCollection<BsonDocument>("guest_recovery_quotas").Find(invalid).AnyAsync(ct))
        {
            throw new InvalidOperationException("Existing guest_recovery_quotas records require review before validation can be installed. No records were modified.");
        }
        await database.RunCommandAsync<BsonDocument>(new BsonDocument
        {
            { "collMod", "guest_recovery_quotas" }, { "validator", validator },
            { "validationLevel", "strict" }, { "validationAction", "error" }
        }, cancellationToken: ct);
    }

    private static BsonDocument Schema() => new()
    {
        { "bsonType", "object" }, { "required", new BsonArray { "_id", "IssuedUtc", "ExpiresUtc", "Version" } },
        { "properties", new BsonDocument
            {
                { "_id", new BsonDocument("bsonType", "string") },
                { "IssuedUtc", new BsonDocument { { "bsonType", "array" }, { "items", new BsonDocument("bsonType", "date") } } },
                { "ExpiresUtc", new BsonDocument("bsonType", "date") },
                { "Version", new BsonDocument("bsonType", new BsonArray { "int", "long" }) }
            }
        }
    };
}
