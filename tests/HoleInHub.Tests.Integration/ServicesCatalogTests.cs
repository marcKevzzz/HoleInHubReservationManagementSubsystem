using HoleInHub.Business.Services;
using HoleInHub.Data.Migrations.Definitions;
using HoleInHub.Data.Services;
using HoleInHub.Tests.Integration.Fixtures;
using MongoDB.Driver;
using Xunit;

namespace HoleInHub.Tests.Integration.Services;

[Trait("Category", "Integration")]
public sealed class ServiceCatalogTests(MongoDbFixture fixture) : IClassFixture<MongoDbFixture>
{
    [Fact]
    public async Task MigrationSeedsSixAndRerunPreservesEditsWhileQueriesRefreshAndSortActiveItems()
    {
        Assert.True(fixture.IsServerAvailable, "Start the isolated local MongoDB replica set.");
        var context = fixture.CreateDbContext();
        var migration = new Migration_008_ServiceCatalog();
        await migration.UpAsync(context.Database, default);
        var catalog = new ServiceCatalog(new ServiceCatalogPersistence(context));
        var original = await catalog.GetActiveAsync(default);
        Assert.Equal(new[] { "Regular", "VIP", "PhilippineCoach", "KpgaPro", "KpgaGroup", "WholeShop" }, original.Select(x => x.Id));
        Assert.Equal("/Home/Contacts", original.Single(x => x.Id == "WholeShop").ActionUrl);
        Assert.Equal("₱600–₱800 / hour", original[0].RateText);
        Assert.Equal("/Reservations/Create?service=KpgaGroup", original.Single(x => x.Id == "KpgaGroup").ActionUrl);

        var collection = context.Database.GetCollection<ServiceCatalogRecord>("service_catalog");
        await collection.UpdateOneAsync(x => x.Id == "Regular", Builders<ServiceCatalogRecord>.Update.Set(x => x.Active, false));
        await collection.UpdateOneAsync(x => x.Id == "VIP", Builders<ServiceCatalogRecord>.Update
            .Set(x => x.Title, "Venue updated VIP offering").Set(x => x.RateText, "Confirm current rate")
            .Set(x => x.Order, -1).Set(x => x.ImagePath, "//external.example/image.webp"));
        await migration.UpAsync(context.Database, default);
        Assert.Equal(6, await collection.CountDocumentsAsync(FilterDefinition<ServiceCatalogRecord>.Empty));
        var refreshed = await catalog.GetActiveAsync(default);
        Assert.Equal(5, refreshed.Count);
        Assert.Equal("Venue updated VIP offering", refreshed[0].Title);
        Assert.Equal("Confirm current rate", refreshed[0].RateText);
        Assert.Equal("/media/facebook/venue-overview.webp", refreshed[0].ImagePath);
        var indexes = await (await collection.Indexes.ListAsync()).ToListAsync();
        Assert.Contains(indexes, x => x["name"] == "ix_service_catalog_active_order_id");
    }
}
