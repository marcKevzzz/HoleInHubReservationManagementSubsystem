using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using HoleInHub.Data.Accounts;
using HoleInHub.Data.Context;
using HoleInHub.Data.Reservations;
using HoleInHub.Web.Accounts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Xunit;
namespace HoleInHub.Tests.Web.Smoke;

[Trait("Category", "Integration")]
public sealed class ActivityAccessTests
{
    private sealed class TestAuth(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var role = Request.Headers["X-Test-Role"].ToString();
            return Task.FromResult(role.Length == 0 ? AuthenticateResult.NoResult() : AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity([
                    new Claim(ClaimTypes.NameIdentifier, "test-user"), new Claim(ClaimTypes.Role, role)
                ], Scheme.Name)), Scheme.Name)));
        }
    }

    [Theory]
    [InlineData("Staff")]
    [InlineData("Manager")]
    [InlineData("Admin")]
    public async Task ActivityProtectsSourcesAndProvidesSafePanelLinks(string role)
    {
        var database = "test_activity_web_" + Guid.NewGuid().ToString("N");
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.UseEnvironment("Testing").ConfigureLogging(l => l.ClearProviders())
            .ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?> { ["MongoDb:ConnectionString"] = "mongodb://localhost:27017/?replicaSet=rs0", ["MongoDb:DatabaseName"] = database, ["MongoDb:ReplicaSetName"] = "rs0", ["Smtp:Host"] = "" }))
            .ConfigureTestServices(services => { services.AddDataProtection().UseEphemeralDataProtectionProvider(); services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, TestAuth>("ActivityTest", _ => { }); services.Configure<CookieAuthenticationOptions>(StaffPinAccess.Scheme, o => o.ForwardDefault = "ActivityTest"); }));
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        var context = factory.Services.GetRequiredService<IMongoDbContext>();
        try
        {
            var booking = new ReservationRecord { Reference = "HIH-ACTIVITY", StartUtc = DateTime.UtcNow.AddDays(1), EndUtc = DateTime.UtcNow.AddDays(1).AddHours(1), History = [new HistoryRecord { AtUtc = DateTime.UtcNow.AddMinutes(-1), Actor = "test-user", Status = "Confirmed", Reason = "Operational reason" }] };
            await context.Database.GetCollection<ReservationRecord>("reservations").InsertOneAsync(booking);
            var audit = new StaffRoleAuditRecord("role-event", "StaffRolesChanged", DateTime.UtcNow.AddSeconds(-30), "actor", "Admin", "target", ["Staff"], ["Manager"], "PRIVATE-ACCESS-REASON", "correlation");
            await context.Database.GetCollection<StaffRoleAuditRecord>("staff_role_audit").InsertOneAsync(audit);
            var response = await client.GetAsync("/staff/activity"); Assert.Equal(HttpStatusCode.OK, response.StatusCode); var html = await response.Content.ReadAsStringAsync(); Assert.Contains("HIH-ACTIVITY", html); Assert.DoesNotContain("PRIVATE-ACCESS-REASON", html);
            client.DefaultRequestHeaders.Add("X-Activity", "list");
            foreach (var category in new[] { "All", "Schedule", role == "Admin" ? "Access" : "Reservations" })
            {
                var filtered = await client.GetAsync($"/staff/activity?range=week&from=2026-11-05&to=2026-11-11&search=&category={category}&actor=everyone");
                Assert.Equal(HttpStatusCode.OK, filtered.StatusCode);
                Assert.Contains("data-activity-results", await filtered.Content.ReadAsStringAsync());
            }
            client.DefaultRequestHeaders.Remove("X-Activity");

            var access = await client.GetAsync("/staff/activity?category=Access"); Assert.Equal(role == "Admin" ? HttpStatusCode.OK : HttpStatusCode.Forbidden, access.StatusCode);
            var service = factory.Services.GetRequiredService<HoleInHub.Business.Activity.ActivityService>();
            var page = await service.QueryAsync(new(DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)), DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1))), true, "test-user", default);
            var accessEntry = page.Entries.Single(e => e.Category == "Access");
            var secret = await client.GetAsync("/staff/activity/" + accessEntry.Key); Assert.Equal(role == "Admin" ? HttpStatusCode.OK : HttpStatusCode.NotFound, secret.StatusCode);
            if (role == "Admin") { Assert.Contains("PRIVATE-ACCESS-REASON", await secret.Content.ReadAsStringAsync()); }
            var operational = page.Entries.Single(e => e.Category == "Reservations"); client.DefaultRequestHeaders.Add("X-Activity", "panel"); var panel = await client.GetStringAsync("/staff/activity/" + operational.Key + "?search=HIH"); Assert.Contains("data-activity-event-content", panel); Assert.DoesNotContain("<html", panel); Assert.Contains("Operational reason", panel); Assert.Contains("activityReturn", panel);
            var targetLink = Regex.Match(panel, "href=\"([^\"]+activityReturn[^\"]+)\"").Groups[1].Value;
            var targetQuery = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(new Uri(new Uri("https://localhost"), WebUtility.HtmlDecode(targetLink)).Query);
            var returnUrl = new Uri(new Uri("https://localhost"), targetQuery["activityReturn"].ToString());
            Assert.Equal("/staff/activity", returnUrl.AbsolutePath);
            var returnQuery = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(returnUrl.Query);
            Assert.Equal("HIH", returnQuery["search"].ToString());
            Assert.Equal(operational.Key, returnQuery["focus"].ToString());
            Assert.DoesNotContain("PRIVATE-ACCESS-REASON", panel);
            client.DefaultRequestHeaders.Remove("X-Activity");
            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/staff/activity?cursor=tampered")).StatusCode);

            if (role == "Staff")
            {
                var rows = Enumerable.Range(1, 51).Select(number => new ReservationRecord
                {
                    Reference = $"HIH-PAGING-{number:D2}",
                    History = [new HistoryRecord { AtUtc = DateTime.UtcNow.AddMinutes(-number), Status = "Confirmed", Actor = "test-user" }]
                });
                await context.Database.GetCollection<ReservationRecord>("reservations").InsertManyAsync(rows);
                var firstPage = await client.GetStringAsync("/staff/activity?search=HIH-PAGING");
                Assert.DoesNotContain("HIH-PAGING-51", firstPage);
                var nextLink = Regex.Match(firstPage, "href=\"([^\"]+)\" data-activity-next");
                Assert.True(nextLink.Success);
                var nextUrl = WebUtility.HtmlDecode(nextLink.Groups[1].Value);
                var secondPage = await client.GetStringAsync(nextUrl);
                Assert.Contains("HIH-PAGING-51", secondPage);
                Assert.DoesNotContain("HIH-PAGING-01", secondPage);
                Assert.Equal(HttpStatusCode.BadRequest,
                    (await client.GetAsync(nextUrl.Replace("search=HIH-PAGING", "search=unrelated"))).StatusCode);
            }
        }
        finally { await context.Client.DropDatabaseAsync(database); }
    }
}
