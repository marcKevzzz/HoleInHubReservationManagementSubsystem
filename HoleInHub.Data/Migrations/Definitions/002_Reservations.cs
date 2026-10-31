using HoleInHub.Data.Reservations;
using MongoDB.Bson;
using MongoDB.Driver;
namespace HoleInHub.Data.Migrations.Definitions;

public sealed class Migration_002_Reservations : IMongoMigration
{
    public string MigrationId => "002_Reservations"; public string Description => "Reservation catalog, guards, schedule blocks and durable notifications"; public string CalculateChecksum() => MongoMigrationChecksum.Compute("002_Reservations:v1");
    public async Task UpAsync(IMongoDatabase database, CancellationToken ct)
    {
        foreach (var name in new[] { "resources", "reservations", "allocation_guards", "schedule_blocks", "outbox" })
        {
            var names = await (await database.ListCollectionNamesAsync(cancellationToken: ct)).ToListAsync(ct); if (!names.Contains(name))
            {
                await database.CreateCollectionAsync(name, cancellationToken: ct);
            }
        }
        await database.GetCollection<ReservationRecord>("reservations").Indexes.CreateManyAsync(new[] { new CreateIndexModel<ReservationRecord>(Builders<ReservationRecord>.IndexKeys.Ascending(x => x.AccountId).Ascending(x => x.IdempotencyKey), new CreateIndexOptions { Unique = true, Name = "ux_account_idempotency" }), new CreateIndexModel<ReservationRecord>(Builders<ReservationRecord>.IndexKeys.Ascending(x => x.Status).Ascending(x => x.StartUtc).Ascending(x => x.EndUtc)), new CreateIndexModel<ReservationRecord>(Builders<ReservationRecord>.IndexKeys.Ascending(x => x.Status).Ascending(x => x.ReviewDeadlineUtc)) }, ct);
        await database.GetCollection<OutboxRecord>("outbox").Indexes.CreateOneAsync(new CreateIndexModel<OutboxRecord>(Builders<OutboxRecord>.IndexKeys.Ascending(x => x.SentUtc).Ascending(x => x.DueUtc).Ascending(x => x.LeaseUntilUtc)), cancellationToken: ct);
        await database.GetCollection<BsonDocument>("allocation_guards").UpdateOneAsync(new BsonDocument("_id", "catalog"), new BsonDocument("$setOnInsert", new BsonDocument("version", 0)), new UpdateOptions { IsUpsert = true }, ct);
        for (var i = 1; i <= 8; i++) { var resource = new ResourceRecord { Id = i <= 6 ? $"bay-{i}" : $"vip-{i}", Name = i <= 6 ? $"Bay {i}" : $"VIP {i}", Service = i <= 6 ? "Regular" : "VIP" }; await database.GetCollection<ResourceRecord>("resources").ReplaceOneAsync(x => x.Id == resource.Id, resource, new ReplaceOptions { IsUpsert = true }, ct); }
        await database.RunCommandAsync<BsonDocument>(new BsonDocument { { "collMod", "reservations" }, { "validator", new BsonDocument("$jsonSchema", new BsonDocument { { "bsonType", "object" }, { "required", new BsonArray { "AccountId", "Email", "Status", "StartUtc", "EndUtc", "ResourceIds", "IdempotencyKey", "PayloadHash" } } }) }, { "validationLevel", "strict" } }, cancellationToken: ct);
    }
}

