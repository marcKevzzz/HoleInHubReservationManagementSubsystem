using HoleInHub.Data.Entities;
using MongoDB.Bson;
using MongoDB.Driver;

namespace HoleInHub.Data.Migrations.Definitions;

public sealed class Migration_001_InitialFoundation : IMongoMigration
{
    public string MigrationId => "001_InitialFoundation";
    public string Description => "Initialize migration tracking collections, unique indexes, and system heartbeat";

    public string CalculateChecksum()
    {
        return MongoMigrationChecksum.Compute("001_InitialFoundation:v1:indexes(__migrations.migrationId,__migration_leases.leaseId)");
    }

    public async Task UpAsync(IMongoDatabase database, CancellationToken ct)
    {
        // 1. Ensure __migrations collection has unique index on migrationId
        var migrationsCollection = database.GetCollection<MigrationRecord>("__migrations");
        var migrationIndexKeys = Builders<MigrationRecord>.IndexKeys.Ascending(x => x.MigrationId);
        var migrationIndexModel = new CreateIndexModel<MigrationRecord>(
            migrationIndexKeys,
            new CreateIndexOptions { Unique = true, Name = "ux_migrations_migrationId" });

        await migrationsCollection.Indexes.CreateOneAsync(migrationIndexModel, cancellationToken: ct);

        // 2. Ensure __migration_leases collection has unique index on leaseId
        var leasesCollection = database.GetCollection<MigrationLease>("__migration_leases");
        var leaseIndexKeys = Builders<MigrationLease>.IndexKeys.Ascending(x => x.LeaseId);
        var leaseIndexModel = new CreateIndexModel<MigrationLease>(
            leaseIndexKeys,
            new CreateIndexOptions { Unique = true, Name = "ux_migration_leases_leaseId" });

        await leasesCollection.Indexes.CreateOneAsync(leaseIndexModel, cancellationToken: ct);

        // 3. Ensure system_heartbeats collection exists and has baseline ping record
        var heartbeatsCollection = database.GetCollection<SystemHeartbeat>("system_heartbeats");
        var filter = Builders<SystemHeartbeat>.Filter.Eq(x => x.Id, SystemHeartbeat.DefaultId);
        var update = Builders<SystemHeartbeat>.Update
            .SetOnInsert(x => x.Id, SystemHeartbeat.DefaultId)
            .Set(x => x.LastPingAtUtc, DateTime.UtcNow)
            .Set(x => x.Environment, "Foundation Initialized");

        await heartbeatsCollection.UpdateOneAsync(
            filter,
            update,
            new UpdateOptions { IsUpsert = true },
            cancellationToken: ct);
    }
}
