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
    public async Task ReorderingLegacyServicesWithoutRevisionPersistsOrderAndRejectsStaleEdits()
    {
        Assert.True(fixture.IsServerAvailable, "Start the isolated local MongoDB replica set.");
        var context = fixture.CreateDbContext();
        await new Migration_008_ServiceCatalog().UpAsync(context.Database, default);
        var collection = context.Database.GetCollection<ServiceCatalogRecord>("service_catalog");
        await collection.UpdateManyAsync(FilterDefinition<ServiceCatalogRecord>.Empty,
            Builders<ServiceCatalogRecord>.Update.Unset(item => item.Revision));
        var catalog = new ServiceCatalog(new ServiceCatalogPersistence(context));
        var original = await catalog.GetAllAsync(default);
        var reordered = original.Reverse().Select(item => new ServicePosition(item.Id, item.Revision)).ToArray();
        await catalog.ReorderAsync(reordered, "admin", default);
        var saved = await catalog.GetAllAsync(default);
        Assert.Equal(reordered.Select(item => item.Id), saved.Select(item => item.Id));
        Assert.All(saved, item => Assert.Equal(1, item.Revision));
        await Assert.ThrowsAsync<InvalidOperationException>(() => catalog.ReorderAsync(
            original.Select(item => new ServicePosition(item.Id, item.Revision)).ToArray(), "admin", default));
    }

    [Fact]
    public async Task CatalogEditsPublishImagesAndDepositsAndRejectStaleChanges()
    {
        Assert.True(fixture.IsServerAvailable, "Start the local MongoDB replica set.");
        var context = fixture.CreateDbContext();
        await new Migration_008_ServiceCatalog().UpAsync(context.Database, default);
        var catalog = new ServiceCatalog(new ServiceCatalogPersistence(context));
        byte[] png = [137, 80, 78, 71, 13, 10, 26, 10, 0, 0, 0, 0];
        var id = Guid.NewGuid().ToString("N");
        var create = new ServiceEdit(id, 0, Guid.NewGuid().ToString("N"), true, "Practice package",
            "A venue offering", "Confirm with venue", "Detailed description", "Regular", "Book", "Book now",
            20, false, "None", 0, []);
        await catalog.SaveAsync(create, [png], "admin", default);
        await catalog.SaveAsync(create, [png], "admin", default);
        Assert.DoesNotContain(await catalog.GetActiveAsync(default), item => item.Id == id);
        var saved = (await catalog.GetAsync(id, default))!;
        Assert.Single(saved.Images);
        var image = await catalog.ImageAsync(saved.Images[0].Split('/').Last(), default);
        Assert.Equal(png, image!.Content);
        var update = saved with { CommandId = Guid.NewGuid().ToString("N"), Active = true, DepositType = "Percentage", DepositValue = 30 };
        await catalog.SaveAsync(update, [], "admin", default);
        var publicItem = Assert.Single(await catalog.GetActiveAsync(default), item => item.Id == id);
        Assert.Contains("30%", publicItem.DepositText);
        Assert.Equal("/Home/Contacts", publicItem.ActionUrl);
        await Assert.ThrowsAsync<InvalidOperationException>(() => catalog.SaveAsync(update with
        { CommandId = Guid.NewGuid().ToString("N"), Title = "Stale" }, [], "admin", default));
        saved = (await catalog.GetAsync(id, default))!;
        await catalog.SetStateAsync(id, saved.Revision, Guid.NewGuid().ToString("N"), "delete", "admin", default);
        Assert.Null(await catalog.GetAsync(id, default));
        Assert.DoesNotContain(await catalog.GetActiveAsync(default), item => item.Id == id);
    }

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
