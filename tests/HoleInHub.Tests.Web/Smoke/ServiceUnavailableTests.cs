using System.Net;
using System.Text.RegularExpressions;
using HoleInHub.Data.Context;
using HoleInHub.Data.Entities;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using Xunit;

namespace HoleInHub.Tests.Web.Smoke;

public sealed class ServiceUnavailableTests
{
    private sealed class OfflineContext : IMongoDbContext
    {
        public IMongoClient Client => throw new TimeoutException("private connection details");
        public IMongoDatabase Database => throw new TimeoutException("private connection details");
        public IMongoCollection<MigrationRecord> Migrations => throw new TimeoutException();
        public IMongoCollection<MigrationLease> MigrationLeases => throw new TimeoutException();
        public IMongoCollection<SystemHeartbeat> SystemHeartbeats => throw new TimeoutException();
        public Task<bool> PingAsync(CancellationToken ct = default) => Task.FromResult(false);
    }
    [Fact]
    public async Task ResetPasswordRetryPreservesLinkButNeverPassword()
    {
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.UseEnvironment("Testing").ConfigureLogging(logging => logging.ClearProviders()).ConfigureServices(services => services.AddSingleton<IMongoDbContext>(new OfflineContext())));
        using var client = factory.CreateClient();
        var form = await client.GetStringAsync("/account/reset-password?userId=sample-user&token=sample-token&returnUrl=%2FReservations%2FCreate");
        var csrf = Regex.Match(form, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        var response = await client.PostAsync("/account/reset-password", new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = WebUtility.HtmlDecode(csrf), ["UserId"] = "sample-user", ["Token"] = "sample-token", ["Password"] = "PrivatePassword123!", ["ConfirmPassword"] = "PrivatePassword123!", ["ReturnUrl"] = "/Reservations/Create" }));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(); Assert.DoesNotContain("PrivatePassword123!", body);
        var retry = WebUtility.HtmlDecode(Regex.Match(body, "href=\"([^\"]+)\">Try again").Groups[1].Value);
        var restored = await client.GetStringAsync(retry);
        Assert.Contains("value=\"sample-user\"", restored); Assert.Contains("value=\"sample-token\"", restored); Assert.Contains("value=\"/Reservations/Create\"", restored);
    }
    [Theory]
    [InlineData("/Reservations/Create", false)]
    [InlineData("/Reservations/CheckAvailability?date=2090-11-05&startTime=14:00&durationMinutes=30&service=Regular", true)]
    public async Task DatabaseFailureReturnsSafe503(string path, bool json)
    {
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.UseEnvironment("Testing").ConfigureLogging(logging => logging.ClearProviders()).ConfigureServices(services => services.AddSingleton<IMongoDbContext>(new OfflineContext())));
        using var client = factory.CreateClient(); var response = await client.GetAsync(path); var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode); Assert.DoesNotContain("private connection details", body); Assert.DoesNotContain("TimeoutException", body);
        Assert.Contains(json ? "message" : "Try again", body);
    }
}
