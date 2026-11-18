using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using HoleInHub.Data.Context;
using HoleInHub.Data.Migrations.Definitions;
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
public sealed class CustomersAccessTests
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
    [InlineData("", HttpStatusCode.Unauthorized)]
    [InlineData("Customer", HttpStatusCode.Forbidden)]
    [InlineData("Staff", HttpStatusCode.OK)]
    [InlineData("Manager", HttpStatusCode.OK)]
    [InlineData("Admin", HttpStatusCode.OK)]
    public async Task CustomerDirectoryAndProfilesRequireAWorkspaceRole(string role, HttpStatusCode expected)
    {
        var database = "test_customers_access_" + Guid.NewGuid().ToString("N");
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder
            .UseEnvironment("Testing").ConfigureLogging(logging => logging.ClearProviders())
            .ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MongoDb:ConnectionString"] = "mongodb://localhost:27017/?replicaSet=rs0",
                ["MongoDb:DatabaseName"] = database,
                ["MongoDb:ReplicaSetName"] = "rs0",
                ["Smtp:Host"] = ""
            }))
            .ConfigureTestServices(services =>
            {
                services.AddDataProtection().UseEphemeralDataProtectionProvider();
                services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, TestAuth>("CustomersTest", _ => { });
                services.Configure<CookieAuthenticationOptions>(StaffPinAccess.Scheme, options => options.ForwardDefault = "CustomersTest");
            }));
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        if (role.Length > 0) { client.DefaultRequestHeaders.Add("X-Test-Role", role); }
        var booking = new ReservationRecord
        {
            AccountId = "customer",
            Reference = "HIH-CUSTOMER",
            FirstName = "Alex",
            LastName = "Santos",
            Email = "alex@example.test",
            Phone = "09175550123",
            Status = "Confirmed",
            Service = "Regular",
            StartUtc = DateTime.UtcNow.AddDays(1),
            EndUtc = DateTime.UtcNow.AddDays(1).AddHours(1),
            CreatedUtc = DateTime.UtcNow,
            ResourceIds = ["bay-2"],
            IdempotencyKey = "test",
            SubmissionHash = "test"
        };
        IMongoDbContext? context = null;
        try
        {
            if (expected == HttpStatusCode.OK)
            {
                context = factory.Services.GetRequiredService<IMongoDbContext>();
                await new Migration_002_Reservations().UpAsync(context.Database, default);
                await context.Database.GetCollection<ReservationRecord>("reservations").InsertOneAsync(booking);
            }
            var list = await client.GetAsync("/staff/customers?search=Alex");
            Assert.Equal(expected, list.StatusCode);
            var profile = await client.GetAsync("/staff/customers/" + booking.Id);
            Assert.Equal(expected, profile.StatusCode);
            if (expected == HttpStatusCode.OK)
            {
                Assert.Contains("Alex Santos", await list.Content.ReadAsStringAsync());
                var html = await profile.Content.ReadAsStringAsync();
                Assert.Contains("Reservation history", html);
                Assert.Contains("customerId=" + booking.Id, html);
                Assert.DoesNotContain("Account history", html);
                var shortSearch = await client.GetStringAsync("/staff/customers?search=m");
                Assert.Contains("Type one more character to search.", shortSearch);
                Assert.DoesNotContain("Enter at least 2 characters", shortSearch);
                Assert.Contains("HIH-CUSTOMER", html);
                Assert.Contains("no-store", profile.Headers.CacheControl!.ToString());
                const string activityReturn = "/staff/activity/reservation:record-0?category=customers&search=Alex";
                var fromActivity = await client.GetStringAsync("/staff/customers/" + booking.Id
                    + "?activityReturn=" + Uri.EscapeDataString(activityReturn));
                Assert.Contains("href=\"/staff/activity/reservation:record-0?category=customers&amp;search=Alex\"", fromActivity);
                Assert.Contains("Back to activity", fromActivity);
                Assert.DoesNotContain("Back to customers", fromActivity);
                var invalidReturn = await client.GetStringAsync("/staff/customers/" + booking.Id
                    + "?activityReturn=" + Uri.EscapeDataString("//evil.test/staff/activity"));
                Assert.DoesNotContain("Back to activity", invalidReturn);
                Assert.Contains("Back to customers", invalidReturn);
                Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/staff/customers/missing")).StatusCode);
            }
        }
        finally { if (context is not null) { await context.Client.DropDatabaseAsync(database); } }
    }

    [Theory]
    [InlineData("Staff")]
    [InlineData("Manager")]
    [InlineData("Admin")]
    public async Task CustomerMergeFlowRequiresAntiforgeryAndCanBeUndone(string role)
    {
        var database = "test_customer_merge_web_" + Guid.NewGuid().ToString("N");
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder
            .UseEnvironment("Testing").ConfigureLogging(logging => logging.ClearProviders())
            .ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MongoDb:ConnectionString"] = "mongodb://localhost:27017/?replicaSet=rs0",
                ["MongoDb:DatabaseName"] = database,
                ["MongoDb:ReplicaSetName"] = "rs0",
                ["Smtp:Host"] = ""
            })).ConfigureTestServices(services =>
            {
                services.AddDataProtection().UseEphemeralDataProtectionProvider();
                services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, TestAuth>("MergeTest", _ => { });
                services.Configure<CookieAuthenticationOptions>(StaffPinAccess.Scheme, options => options.ForwardDefault = "MergeTest");
            }));
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        var context = factory.Services.GetRequiredService<IMongoDbContext>();
        var a = new ReservationRecord { AccountId = "account-a", Reference = "HIH-MERGE-A", FirstName = "Alex", LastName = "Santos", Email = "alex@example.test", Phone = "09175550123", Status = "Confirmed", Service = "Regular", StartUtc = DateTime.UtcNow.AddDays(2), EndUtc = DateTime.UtcNow.AddDays(2).AddHours(1), CreatedUtc = DateTime.UtcNow, ResourceIds = ["bay-2"], IdempotencyKey = "a", SubmissionHash = "a" };
        var b = new ReservationRecord { AccountId = "staff:employee", Reference = "HIH-MERGE-B", FirstName = "Alexander", LastName = "Santos", Email = "alex.other@example.test", Phone = "09175550123", Status = "Confirmed", Service = "Regular", StartUtc = DateTime.UtcNow.AddDays(3), EndUtc = DateTime.UtcNow.AddDays(3).AddHours(1), CreatedUtc = DateTime.UtcNow, ResourceIds = ["bay-2"], IdempotencyKey = "b", SubmissionHash = "b" };
        try
        {
            await new Migration_002_Reservations().UpAsync(context.Database, default);
            await context.Database.GetCollection<ReservationRecord>("reservations").InsertManyAsync([a, b]);
            await new Migration_018_CustomerProfiles().UpAsync(context.Database, default);
            var profileId = (await new HoleInHub.Data.Customers.CustomerProfilePersistence(context).ReadAsync(a.Id, default))!.Profile.Id;
            var details = await client.GetStringAsync($"/staff/customers/{a.Id}");
            Assert.Contains("customer-profile-header", details);
            Assert.Contains("aria-label=\"New booking\"", details);
            Assert.DoesNotContain("Account history", details);
            Assert.Contains($"href=\"/staff?customerProfileId={profileId}\"", details);
            var schedule = await client.GetStringAsync($"/staff?date=2026-11-18&customerProfileId={profileId}");
            Assert.Contains($"data-customer-profile-id=\"{profileId}\"", schedule);
            Assert.Contains("Drag an open time", schedule);
            Assert.Contains("value=\"Alex\"", schedule);
            Assert.Contains("value=\"09175550123\"", schedule);
            Assert.Contains("customerProfileId=" + profileId, schedule);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/staff?customerProfileId=missing")).StatusCode);
            var create = await client.GetStringAsync($"/staff/reservations/new?customerProfileId={profileId}");
            Assert.Contains("staff-new-booking-page", create);
            Assert.Contains("Back to customer", create);
            Assert.Contains($"href=\"/staff/customers/{profileId}\"", create);
            Assert.DoesNotContain("Back to reservations", create);
            var directory = await client.GetStringAsync("/staff/customers");
            Assert.Contains("data-merge-handle", directory);
            Assert.Contains("data-customer-row-merge", directory);
            Assert.DoesNotContain("data-row-merge-action", directory);
            var picker = await client.GetStringAsync($"/staff/customers/{a.Id}/merge");
            Assert.Contains("people-search", picker);
            Assert.Contains("data-customer-results", picker);
            Assert.Contains("js/customer-search.js", picker);
            Assert.DoesNotContain(">Search</button>", picker);
            var path = $"/staff/customers/{a.Id}/merge?target={b.Id}";
            client.DefaultRequestHeaders.Add("X-Customer-Merge", "dialog");
            var dialog = await client.GetStringAsync(path);
            Assert.Contains("data-row-merge-content", dialog);
            Assert.DoesNotContain("<html", dialog);
            Assert.DoesNotContain("<dialog", dialog);
            Assert.Contains("data-merge-confirmation", dialog);
            Assert.Contains("merge-field-options", dialog);
            Assert.Contains("data-merge-included", dialog);
            Assert.DoesNotContain("merge-selected-bookings", dialog);
            Assert.DoesNotContain("Inspect source history", dialog);
            Assert.DoesNotContain("Use as primary", dialog);
            Assert.Single(Regex.Matches(dialog, "name=\"Form.PhoneFrom\"").Cast<Match>());
            client.DefaultRequestHeaders.Remove("X-Customer-Merge");
            var html = await client.GetStringAsync(path);
            Assert.Contains("Compare the records", html);
            Assert.Contains("Customer account access and original booking details stay the same.", html);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync(path, new FormUrlEncodedContent([]))).StatusCode);
            var fields = FormFields(html, "customer-merge-form");
            fields["Form.Reason"] = "Verified duplicate at the venue"; fields["Form.BookingScope"] = "all";
            fields["Form.NameFrom"] = "target"; fields["Form.PhoneFrom"] = "target"; fields["Form.EmailFrom"] = "target";
            var saved = await client.PostAsync(path, new FormUrlEncodedContent(fields));
            Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);
            Assert.Equal(saved.Headers.Location, (await client.PostAsync(path, new FormUrlEncodedContent(fields))).Headers.Location);
            client.DefaultRequestHeaders.Add("X-Customer-Merge", "dialog");
            var replay = await client.PostAsync(path, new FormUrlEncodedContent(fields));
            Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
            Assert.Contains("application/json", replay.Content.Headers.ContentType!.ToString());
            using var replayJson = JsonDocument.Parse(await replay.Content.ReadAsStringAsync());
            Assert.NotNull(replayJson.RootElement.GetProperty("detailsUrl").GetString());
            var toast = replayJson.RootElement.GetProperty("toast");
            Assert.Equal("success", toast.GetProperty("kind").GetString());
            Assert.Equal("Customer records merged", toast.GetProperty("title").GetString());
            Assert.Equal("1 reservation added to the staff profile.", toast.GetProperty("message").GetString());
            Assert.Equal("View merge history", toast.GetProperty("action").GetString());
            Assert.EndsWith("/merge-history", toast.GetProperty("href").GetString());
            client.DefaultRequestHeaders.Remove("X-Customer-Merge");
            var profile = await client.GetStringAsync(saved.Headers.Location);
            Assert.Contains("data-toast-seed data-kind=\"success\"", profile);
            Assert.Contains("Customer records merged", profile);
            Assert.DoesNotContain("data-toast-action aria-label=", profile);
            Assert.DoesNotContain("data-toast-seed", await client.GetStringAsync(saved.Headers.Location));
            Assert.DoesNotContain("Combined staff history", profile);
            Assert.DoesNotContain("Manually combined for staff.", profile);
            Assert.Contains("Contact details", profile);
            var contacts = Regex.Match(profile, "<aside[^>]*aria-labelledby=\"customer-contact-heading\"[^>]*>(.*?)</aside>", RegexOptions.Singleline).Groups[1].Value;
            Assert.NotEmpty(contacts);
            Assert.Equal(2, Regex.Matches(contacts, "href=\"mailto:").Count);
            Assert.Single(Regex.Matches(contacts, "href=\"tel:").Cast<Match>());
            Assert.Single(await new ReservationPersistence(context).OwnedAsync("account-a", default));
            var root = (await new HoleInHub.Data.Customers.CustomerProfilePersistence(context).ReadAsync(a.Id, default))!.Profile.Id;
            var history = await client.GetStringAsync($"/staff/customers/{root}/merge-history");
            var undo = FormFields(history, "undo-" + fields["Form.OperationId"]);
            undo["Form.Reason"] = "Reverse the test consolidation";
            var reversed = await client.PostAsync($"/staff/customers/{root}/undo-merge", new FormUrlEncodedContent(undo));
            Assert.Equal(HttpStatusCode.Redirect, reversed.StatusCode);
            var undoFeedback = await client.GetStringAsync(reversed.Headers.Location);
            Assert.Contains("data-toast-seed data-kind=\"success\"", undoFeedback);
            Assert.Contains("Merge undone", undoFeedback);
            Assert.DoesNotContain("data-toast-seed", await client.GetStringAsync(reversed.Headers.Location));
            Assert.Single((await new HoleInHub.Data.Customers.CustomerProfilePersistence(context).ReadAsync(a.Id, default))!.ReservationIds);
            Assert.Equal("Alexander", (await context.Database.GetCollection<ReservationRecord>("reservations").Find(x => x.Id == b.Id).SingleAsync()).FirstName);
        }
        finally { await context.Client.DropDatabaseAsync(database); }
    }
    private static Dictionary<string, string> FormFields(string html, string id)
    {
        var form = Regex.Match(html, "<form[^>]*id=\"" + Regex.Escape(id) + "\"[^>]*>(.*?)</form>", RegexOptions.Singleline).Groups[1].Value;
        Assert.NotEmpty(form);
        var fields = new Dictionary<string, string>();
        foreach (Match input in Regex.Matches(form, "<input[^>]*>", RegexOptions.Singleline))
        {
            if (!input.Value.Contains("type=\"hidden\"", StringComparison.Ordinal)) { continue; }
            var name = Regex.Match(input.Value, "name=\"([^\"]+)\"").Groups[1].Value;
            var value = Regex.Match(input.Value, "value=\"([^\"]*)\"").Groups[1].Value;
            if (name.Length > 0) { fields[name] = WebUtility.HtmlDecode(value); }
        }
        return fields;
    }
}
