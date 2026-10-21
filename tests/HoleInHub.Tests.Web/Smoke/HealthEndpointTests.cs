using System.Net;
using System.Text.Json;
using FluentAssertions;
using HoleInHub.Business.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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

    [Theory]
    [InlineData(true, HttpStatusCode.OK, "Healthy")]
    [InlineData(false, HttpStatusCode.ServiceUnavailable, "Unhealthy")]
    public async Task GetHealthReady_ReturnsServiceReadiness(bool ready, HttpStatusCode expectedCode, string expectedStatus)
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IHealthService>();
                services.AddSingleton<IHealthService>(new StubHealthService(ready));
            }));
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/health/ready");

        response.StatusCode.Should().Be(expectedCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("status").GetString().Should().Be(expectedStatus);
        body.RootElement.GetProperty("description").GetString().Should().Be("Test database readiness");
        body.RootElement.GetProperty("timestampUtc").GetDateTime().Kind.Should().Be(DateTimeKind.Utc);
    }

    private sealed class StubHealthService(bool ready) : IHealthService
    {
        public Task<bool> CheckDatabaseLivenessAsync(CancellationToken ct = default) => Task.FromResult(ready);

        public Task<(bool IsReady, string StatusDescription)> CheckDatabaseReadinessAsync(CancellationToken ct = default) =>
            Task.FromResult((ready, "Test database readiness"));
    }
}
