using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace HoleInHub.Tests.Web.Smoke;

public sealed class HomePageSmokeTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public HomePageSmokeTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetHomePage_ReturnsOkAndContainsBrandName()
    {
        // Act
        var response = await _client.GetAsync("/");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().ContainEquivalentOf("Hole In Hub");
    }


    [Fact]
    public async Task GetPrivacyPage_ReturnsOk()
    {
        // Act
        var response = await _client.GetAsync("/Home/Privacy");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
