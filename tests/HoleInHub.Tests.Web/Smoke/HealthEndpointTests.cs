using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace HoleInHub.Tests.Web.Smoke;

public sealed class HealthEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public HealthEndpointTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetHealthLive_ReturnsOkWithHealthyStatus()
    {
        // Act
        var response = await _client.GetAsync("/health/live");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("\"status\":\"Healthy\"");
    }

    [Fact]
    public async Task AnyRequest_ReturnsCorrelationIdHeader()
    {
        // Act
        var response = await _client.GetAsync("/health/live");

        // Assert
        response.Headers.Should().ContainKey("X-Correlation-ID");
        var correlationId = response.Headers.GetValues("X-Correlation-ID").FirstOrDefault();
        correlationId.Should().NotBeNullOrWhiteSpace();
    }
}
