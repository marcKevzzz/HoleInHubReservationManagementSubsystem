using HoleInHub.Business.Services;
using HoleInHub.Data.Migrations.Definitions;
using HoleInHub.Data.Services;
using HoleInHub.Tests.Integration.Fixtures;
using Xunit;

namespace HoleInHub.Tests.Integration.Services;

[Trait("Category", "Integration")]
public sealed class SeasonalActivationTests(MongoDbFixture fixture) : IClassFixture<MongoDbFixture>
{
    [Fact]
    public async Task SettingSeasonActivatesEachDraftAndPreservesLegacyState()
    {
        Assert.True(fixture.IsServerAvailable);
        var context = fixture.CreateDbContext();
        await new Migration_002_Reservations().UpAsync(context.Database, default);
        await new Migration_008_ServiceCatalog().UpAsync(context.Database, default);
        await new Migration_028_SeasonalRentals().UpAsync(context.Database, default);
        var catalog = new ServiceCatalog(new ServiceCatalogPersistence(context));
        var start = new DateOnly(2026, 12, 1);
        var end = start.AddDays(30);
        foreach (var id in new[] { SeasonalRental.Whole, SeasonalRental.Regular, SeasonalRental.Vip })
        {
            var draft = (await catalog.GetAsync(id, default))!;
            Assert.False(draft.Active);
            await catalog.SetStateAsync(id, draft.Revision, Guid.NewGuid().ToString("N"), "activate", "admin", default, start, end);
            var saved = (await catalog.GetAsync(id, default))!;
            Assert.True(saved.Active);
            Assert.Equal(start, saved.SeasonStart);
            Assert.Equal(end, saved.SeasonEnd);
        }
        Assert.False((await catalog.GetAsync("WholeShop", default))!.Active);
    }
}
