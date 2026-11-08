using HoleInHub.Data.Accounts;
using MongoDB.Driver;

namespace HoleInHub.Data.Migrations.Definitions;

public sealed class Migration_016_StaffPinAttempts : IMongoMigration
{
    public string MigrationId => "016_StaffPinAttempts";
    public string Description => "Expire desktop and employee PIN attempt counters";
    public string CalculateChecksum() => MongoMigrationChecksum.Compute(MigrationId + ":v1:staff_pin_attempts:ExpiresUtc:ttl");

    public Task UpAsync(IMongoDatabase database, CancellationToken ct) =>
        database.GetCollection<StaffPinAttempt>("staff_pin_attempts").Indexes.CreateOneAsync(
            new CreateIndexModel<StaffPinAttempt>(Builders<StaffPinAttempt>.IndexKeys.Ascending(entry => entry.ExpiresUtc),
                new CreateIndexOptions { Name = "ttl_staff_pin_attempts", ExpireAfter = TimeSpan.Zero }), cancellationToken: ct);
}
