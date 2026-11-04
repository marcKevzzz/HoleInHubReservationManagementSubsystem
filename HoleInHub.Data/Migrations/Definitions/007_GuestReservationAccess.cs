using HoleInHub.Data.Reservations;
using MongoDB.Bson;
using MongoDB.Driver;

namespace HoleInHub.Data.Migrations.Definitions;

public sealed class Migration_007_GuestReservationAccess : IMongoMigration
{
    public string MigrationId => "007_GuestReservationAccess";
    public string Description => "Guest recovery purpose, email history index and durable recovery delivery limits";
    public string CalculateChecksum() => MongoMigrationChecksum.Compute(MigrationId + ":v1:GuestRecovery:email-start-id-en-secondary:recovery_quotas-ttl");

    public async Task UpAsync(IMongoDatabase database, CancellationToken ct)
    {
        // Extend the installed validator without changing any of migration 006's other constraints.
        using var collections = await database.ListCollectionsAsync(new ListCollectionsOptions
        { Filter = new BsonDocument("name", "guest_verifications") }, ct);
        var collection = await collections.FirstAsync(ct);
        var validator = collection["options"]["validator"].AsBsonDocument.DeepClone().AsBsonDocument;
        validator["$jsonSchema"]["properties"]["Purpose"]["enum"] = new BsonArray { "GuestBooking", "AccountEmail", "GuestRecovery" };
        await database.RunCommandAsync<BsonDocument>(new BsonDocument
        { { "collMod", "guest_verifications" }, { "validator", validator }, { "validationLevel", "strict" }, { "validationAction", "error" } }, cancellationToken: ct);
        await database.GetCollection<ReservationRecord>("reservations").Indexes.CreateOneAsync(new CreateIndexModel<ReservationRecord>(
            Builders<ReservationRecord>.IndexKeys.Ascending(x => x.Email).Descending(x => x.StartUtc).Descending(x => x.Id),
            new CreateIndexOptions { Name = "ix_guest_email_start_id", Collation = ReservationPersistence.GuestEmailCollation }), cancellationToken: ct);

        var names = await (await database.ListCollectionNamesAsync(cancellationToken: ct)).ToListAsync(ct);
        if (!names.Contains("guest_recovery_quotas"))
        {
            await database.CreateCollectionAsync("guest_recovery_quotas", new CreateCollectionOptions<BsonDocument>
            {
                Validator = new BsonDocument("$jsonSchema", new BsonDocument
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
                })
            }, ct);
        }
        await database.GetCollection<BsonDocument>("guest_recovery_quotas").Indexes.CreateOneAsync(new CreateIndexModel<BsonDocument>(
            Builders<BsonDocument>.IndexKeys.Ascending("ExpiresUtc"), new CreateIndexOptions { Name = "ttl_guest_recovery_quotas", ExpireAfter = TimeSpan.Zero }), cancellationToken: ct);
    }
}
