using System.Net;
using HoleInHub.Data.Context;
using HoleInHub.Data.Migrations.Definitions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace HoleInHub.Tests.Web.Smoke;

[Trait("Category", "Integration")]
public sealed class AvailabilityPageTests
{
    [Fact]
    public async Task SelectedIntervalAndBayAreCarriedIntoReservationLinks()
    {
        var databaseName = $"hih_availability_{Guid.NewGuid():N}";
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseEnvironment("Testing").ConfigureLogging(logging => logging.ClearProviders())
                .ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["MongoDb:ConnectionString"] = "mongodb://localhost:27017/?replicaSet=rs0",
                    ["MongoDb:DatabaseName"] = databaseName,
                    ["MongoDb:ReplicaSetName"] = "rs0"
                })));
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IMongoDbContext>();
        try
        {
            await new Migration_002_Reservations().UpAsync(context.Database, CancellationToken.None);
            using var client = factory.CreateClient();
            var date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)).ToString("yyyy-MM-dd");
            var response = await client.GetAsync($"/availability?date={date}&startTime=14:30&durationMinutes=90&service=VIP");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
            Assert.Contains("YOUR SPACE TO PLAY", html);
            Assert.Contains("2 of 2 vip bays available", html);
            Assert.Contains($"date={date}", html);
            Assert.Contains("durationMinutes=90", html);
            Assert.Contains("service=VIP", html);
            Assert.Contains("resourceId=vip-7", html);
            Assert.Contains("resourceId=vip-8", html);
            Assert.DoesNotContain("resourceId=bay-1", html);
            var booking = WebUtility.HtmlDecode(await client.GetStringAsync($"/Reservations/Create?date={date}&service=VIP&resourceId=vip-7"));
            Assert.Contains("id=\"booking-auth-dialog\"", booking);
            Assert.Contains("CONTINUE BROWSING", booking);
            Assert.Contains("KPGA lesson program", booking);
            Assert.Contains("Rent the whole shop", booking);
            Assert.DoesNotContain("Sign in to use your verified email", booking);
            var invalid = WebUtility.HtmlDecode(await client.GetStringAsync($"/availability?date={date}&startTime=23:30&durationMinutes=90&service=VIP"));
            Assert.Contains("LET’S FIND ANOTHER TIME.", invalid);
            Assert.DoesNotContain("Choose this bay", invalid);
            Assert.DoesNotContain("PLAN YOUR ROUND", invalid);
        }
        finally
        {
            await context.Client.DropDatabaseAsync(databaseName);
        }
    }
}
