using HoleInHub.Data.Services;
using MongoDB.Driver;

namespace HoleInHub.Data.Migrations.Definitions;

public sealed class Migration_030_JuniorGolfProgram : IMongoMigration
{
    public string MigrationId => "030_JuniorGolfProgram";
    public string Description => "Set junior eligibility on the existing 5th Junior Golf Program";
    public string CalculateChecksum() => MongoMigrationChecksum.Compute(MigrationId + ":v1:156e2aa104ec46f1bfa01ecec66e4219");

    public async Task UpAsync(IMongoDatabase database, CancellationToken ct)
    {
        var catalog = database.GetCollection<ServiceCatalogRecord>("service_catalog");
        await catalog.UpdateOneAsync(x => x.Id == "156e2aa104ec46f1bfa01ecec66e4219" &&
            x.BookingService == "KpgaGroup" && !x.Deleted && x.JuniorOnly != true,
            Builders<ServiceCatalogRecord>.Update.Set(x => x.JuniorOnly, true).Inc(x => x.Revision, 1), cancellationToken: ct);
    }
}
