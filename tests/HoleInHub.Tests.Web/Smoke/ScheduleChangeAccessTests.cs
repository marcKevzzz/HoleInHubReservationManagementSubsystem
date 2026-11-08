using System.Net;
using System.Text.RegularExpressions;
using HoleInHub.Business.Reservations;
using HoleInHub.Data.Context;
using HoleInHub.Data.Migrations.Definitions;
using HoleInHub.Data.Reservations;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using Xunit;

namespace HoleInHub.Tests.Web.Smoke;

[Trait("Category", "Integration")]
public sealed class ScheduleChangeAccessTests
{
    [Fact]
    public async Task SecureLinkCanProposeButReferenceAloneCannotAndAntiforgeryIsRequired()
    {
        var database = "test_change_access_" + Guid.NewGuid().ToString("N");
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseEnvironment("Testing").ConfigureLogging(logging => logging.ClearProviders())
                .ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["MongoDb:ConnectionString"] = "mongodb://localhost:27017/?replicaSet=rs0",
                    ["MongoDb:DatabaseName"] = database,
                    ["MongoDb:ReplicaSetName"] = "rs0",
                    ["Smtp:Host"] = "",
                    ["ReservationPolicy:Approved"] = "true",
                    ["ReservationPolicy:DurationsApproved"] = "true",
                    ["ReservationPolicy:ReviewStart"] = "10:00:00",
                    ["ReservationPolicy:ReviewEnd"] = "1.00:00:00",
                    ["ReservationPolicy:ReviewDays:0"] = "0",
                    ["ReservationPolicy:ReviewDays:1"] = "1",
                    ["ReservationPolicy:ReviewDays:2"] = "2",
                    ["ReservationPolicy:ReviewDays:3"] = "3",
                    ["ReservationPolicy:ReviewDays:4"] = "4",
                    ["ReservationPolicy:ReviewDays:5"] = "5",
                    ["ReservationPolicy:ReviewDays:6"] = "6",
                    ["ReservationPolicy:StaffEmails:0"] = "staff@example.test"
                })).ConfigureTestServices(services => services.AddDataProtection().UseEphemeralDataProtectionProvider()));
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        var context = factory.Services.GetRequiredService<IMongoDbContext>();
        try
        {
            await new Migration_002_Reservations().UpAsync(context.Database, default);
            var token = new string('a', 64);
            var booking = new ReservationRecord
            {
                AccountId = "guest:example",
                Email = "guest@example.test",
                Service = "Regular",
                Status = "Confirmed",
                ResourceIds = ["bay-2"],
                StartUtc = DateTime.UtcNow.Date.AddDays(3).AddHours(4),
                EndUtc = DateTime.UtcNow.Date.AddDays(3).AddHours(5),
                AccessTokenHash = GuestBookingService.Hash(token),
                IdempotencyKey = Guid.NewGuid().ToString("N"),
                SubmissionHash = "initial",
                Reference = "HIH-ACCESS",
                Attendees = 1
            };
            await context.Database.GetCollection<ReservationRecord>("reservations").InsertOneAsync(booking);
            var path = "/Reservations/Change?id=" + booking.Id;
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(path)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(path + "&token=" + new string('b', 64))).StatusCode);
            var page = await client.GetAsync(path + "&token=" + token);
            Assert.Equal(HttpStatusCode.OK, page.StatusCode);
            var html = await page.Content.ReadAsStringAsync();
            Assert.Contains("Your current booking stays reserved", html);
            Assert.Contains("ExpectedRevision", html);
            Assert.NotEmpty(Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync(path + "&token=" + token, new FormUrlEncodedContent([]))).StatusCode);
            var csrf = WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
            var proposedDate = TimeZoneInfo.ConvertTimeFromUtc(booking.StartUtc, ReservationRules.Manila).AddDays(1).ToString("yyyy-MM-dd");
            var changed = await client.PostAsync(path + "&token=" + token, new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = csrf,
                ["ExpectedRevision"] = "0",
                ["CommandId"] = Guid.NewGuid().ToString("N"),
                ["Date"] = proposedDate,
                ["StartTime"] = "14:00",
                ["DurationMinutes"] = "90",
                ["Reason"] = "Please move our visit."
            }));
            Assert.Equal(HttpStatusCode.Redirect, changed.StatusCode);
            var stored = await context.Database.GetCollection<ReservationRecord>("reservations").Find(x => x.Id == booking.Id).SingleAsync();
            Assert.Equal("Pending", stored.ScheduleChange!.Status);
            Assert.Equal(booking.StartUtc, stored.StartUtc);
            var staff = await client.PostAsync("/BookingReview/DecideChange", new FormUrlEncodedContent([]));
            Assert.True(staff.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.Unauthorized);
        }
        finally { await context.Client.DropDatabaseAsync(database); }
    }
}
