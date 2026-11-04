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
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/Reservations/Availability")).StatusCode);
            Assert.DoesNotContain("href=\"/Reservations/Availability\"", await client.GetStringAsync("/Home/About"));
            var booking = WebUtility.HtmlDecode(await client.GetStringAsync($"/Reservations/Create?date={date}&service=VIP&resourceId=vip-7"));
            Assert.Contains("id=\"booking-auth-dialog\"", booking);
            Assert.Contains("Continue as guest", booking);
            Assert.Contains("Number of guests", booking);
            Assert.DoesNotContain("name=\"verificationCode\"", booking);
            Assert.Contains("id=\"booking-submit\"", booking);
            Assert.Contains("KPGA lesson program", booking);
            Assert.Contains("Rent the whole shop", booking);
            Assert.DoesNotContain("Sign in to use your verified email", booking);
        }
        finally
        {
            await context.Client.DropDatabaseAsync(databaseName);
        }
    }
}
