using MongoDB.Bson;
using MongoDB.Driver;

namespace HoleInHub.Data.Migrations.Definitions;

public sealed class Migration_026_ClassPackages : IMongoMigration
{
    public string MigrationId => "026_ClassPackages";
    public string Description => "Index fixed-date lesson packages and class roster lookups";
    public string CalculateChecksum() => MongoMigrationChecksum.Compute(MigrationId + ":package-id:offering-open:roster:v1");

    public async Task UpAsync(IMongoDatabase database, CancellationToken ct)
    {
        var packages = database.GetCollection<BsonDocument>("class_packages");
        var keys = Builders<BsonDocument>.IndexKeys;
        await packages.Indexes.CreateManyAsync([
            new(keys.Ascending("package_id"), new CreateIndexOptions { Unique = true, Name = "ux_class_package_id" }),
            new(keys.Ascending("ServiceEntryId").Ascending("Open"), new CreateIndexOptions { Name = "ix_package_offering_open" })
        ], ct);
        await database.GetCollection<BsonDocument>("reservations").Indexes.CreateOneAsync(
            new CreateIndexModel<BsonDocument>(keys.Ascending("PackageDates.class_session_id"), new CreateIndexOptions { Sparse = true, Name = "ix_package_roster" }), cancellationToken: ct);
    }
}
