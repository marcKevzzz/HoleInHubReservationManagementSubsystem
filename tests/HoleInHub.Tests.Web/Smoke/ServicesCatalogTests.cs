using System.Net;
using HoleInHub.Business.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace HoleInHub.Tests.Web.Smoke;

public sealed class ServicesCatalogTests
{
    private sealed class CurrentCatalog : IServiceCatalog
    {
        public string Title { get; set; } = "Current venue service";
        public bool Offline { get; set; }
        public int Reads { get; private set; }
        public Task<IReadOnlyList<ServiceCatalogItem>> GetActiveAsync(CancellationToken ct)
        {
            Reads++;
            if (Offline) { throw new ServiceUnavailableException(); }
            return Task.FromResult<IReadOnlyList<ServiceCatalogItem>>([new("Regular", Title,
                "/media/facebook/venue-overview.webp", "Five bays for your group", "Confirm current rate",
                "Venue confirms each booking", "Request a bay", "/Reservations/Create?service=Regular")]);
        }
    }

    [Fact]
    public async Task FullPageAndPartialFetchCurrentCatalogEachTimeAndRenderSafeContent()
    {
        var catalog = new CurrentCatalog();
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder
            .ConfigureLogging(logging => logging.ClearProviders())
            .ConfigureServices(services => services.AddSingleton<IServiceCatalog>(catalog)));
        using var client = factory.CreateClient();
        var first = await client.GetAsync("/Home/Services");
        var firstHtml = await first.Content.ReadAsStringAsync();
        Assert.Contains("MORE WAYS TO PLAY", firstHtml);
        Assert.Contains("Current venue service", firstHtml);
        Assert.Contains("/js/services.js", firstHtml);
        Assert.True(first.Headers.CacheControl?.NoStore);
        catalog.Title = "Updated venue service <script>unsafe</script>";
        var secondHtml = await client.GetStringAsync("/Home/Services");
        var partial = await client.GetAsync("/Home/ServiceCatalog");
        var partialHtml = await partial.Content.ReadAsStringAsync();
        Assert.Contains("Updated venue service &lt;script&gt;unsafe&lt;/script&gt;", secondHtml);
        Assert.Contains("Updated venue service &lt;script&gt;unsafe&lt;/script&gt;", partialHtml);
        Assert.DoesNotContain("<html", partialHtml);
        Assert.True(partial.Headers.CacheControl?.NoStore);
        Assert.Equal(3, catalog.Reads);
        catalog.Offline = true;
        var unavailable = await client.GetAsync("/Home/ServiceCatalog");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, unavailable.StatusCode);
        var recovery = await client.GetStringAsync("/Home/Services");
        Assert.Contains("Services are temporarily unavailable", recovery);
        Assert.Contains("/Home/Contacts", recovery);
    }
}
