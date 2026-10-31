using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging;
using Xunit;
namespace HoleInHub.Tests.Web.Smoke;

public sealed class PublicPagesSmokeTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;
    public PublicPagesSmokeTests(WebApplicationFactory<Program> factory) { _client = factory.WithWebHostBuilder(builder => builder.ConfigureLogging(logging => logging.ClearProviders())).CreateClient(); }
    [Theory]
    [InlineData("/Home/About", "THE HOLE IN HUB EXPERIENCE")]
    [InlineData("/Home/Gallery", "INSIDE HOLE IN HUB")]
    [InlineData("/Home/Contacts", "OPERATING HOURS")]
    [InlineData("/Home/Services", "MORE WAYS TO PLAY")]
    public async Task Public_page_contains_real_content(string path, string heading)
    {
        var response = await _client.GetAsync(path);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain(heading).And.Contain("/media/brand/wordmark-hq.webp");
        html.Should().NotContain("Simulators Live").And.NotContain("POS Simulation");
    }

    [Fact]
    public async Task Motion_assets_load_in_dependency_order_with_native_navigation_fallback()
    {
        var html = await _client.GetStringAsync("/");
        html.Should().Contain("<details id=\"site-menu\"").And.Contain("class=\"smooth-wrapper\"").And.Contain("class=\"smooth-content\"");
        html.IndexOf("/js/vendor/gsap.min.js", StringComparison.Ordinal).Should().BeLessThan(html.IndexOf("/js/vendor/ScrollTrigger.min.js", StringComparison.Ordinal));
        html.IndexOf("/js/vendor/ScrollTrigger.min.js", StringComparison.Ordinal).Should().BeLessThan(html.IndexOf("/js/vendor/ScrollSmoother.min.js", StringComparison.Ordinal));
        html.IndexOf("/js/vendor/ScrollSmoother.min.js", StringComparison.Ordinal).Should().BeLessThan(html.IndexOf("/js/motion.js", StringComparison.Ordinal));
        html.IndexOf("<header", StringComparison.Ordinal).Should().BeLessThan(html.IndexOf("class=\"smooth-wrapper\"", StringComparison.Ordinal));
    }
}
