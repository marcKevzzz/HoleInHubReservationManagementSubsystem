using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using HoleInHub.Data.Context;
using HoleInHub.Data.Reservations;
using Microsoft.AspNetCore.Authentication;
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

public sealed class AccountMenuTests
{
    [Fact]
    public async Task GuestMenuOffersSignInAndCreateAccount()
    {
        await using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();
        var html = await client.GetStringAsync("/");
        Assert.Contains("id=\"account-menu\"", html);
        Assert.Contains("popover=\"auto\"", html);
        Assert.DoesNotContain("Sign in to manage your reservations and plan your next visit.", html);
        Assert.Matches("class=\"button button-tertiary\"[^>]*data-no-icon[^>]*href=\"/account/sign-up\"", html);
        Assert.Contains("/js/page-transitions.js", html);
        Assert.Contains("/js/ui-motion.js", html);
        Assert.True(html.IndexOf("/js/ui-motion.js", StringComparison.Ordinal) < html.IndexOf("/js/motion.js", StringComparison.Ordinal));
        Assert.True(html.IndexOf("/js/vendor/ScrollTrigger.min.js", StringComparison.Ordinal) < html.IndexOf("/js/motion.js", StringComparison.Ordinal));
        Assert.DoesNotContain("action=\"/account/sign-out\"", html);
    }

    [Fact]
    public async Task SignedInMenuOffersReservationsAndProtectedLogout()
    {
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.AddAuthentication("MenuTest")
                .AddScheme<AuthenticationSchemeOptions, MenuAuthenticationHandler>("MenuTest", _ => { })));
        using var client = factory.CreateClient();
        var html = await client.GetStringAsync("/");
        Assert.Contains("href=\"/my-reservations\"", html);
        Assert.Contains("href=\"/transactions\"", html);
        Assert.Contains("href=\"/my-profile\"", html);
        Assert.DoesNotContain("data-account-placeholder", html);
        Assert.Contains("action=\"/account/sign-out\"", html);
        Assert.Contains("__RequestVerificationToken", html);
        Assert.Contains("id=\"logout-confirmation\"", html);
        Assert.Contains("data-confirm-logout", html);
        Assert.DoesNotContain("account-menu-secondary", html);
        Assert.DoesNotContain("Staff workspace", html);
        Assert.Contains("Admin workspace", html);
        Assert.DoesNotContain("href=\"/notifications\"", html);
    }

    [Theory]
    [InlineData("Staff", "Staff workspace", "/staff")]
    [InlineData("Manager", "Manager workspace", "/staff/dashboard")]
    [InlineData("Admin", "Admin workspace", "/staff/dashboard")]
    public async Task WorkspaceReturnAppearsAfterMyProfile(string role, string label, string destination)
    {
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.AddAuthentication("MenuTest")
                .AddScheme<AuthenticationSchemeOptions, MenuAuthenticationHandler>("MenuTest", _ => { })));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Menu-Role", role);
        var html = await client.GetStringAsync("/");
        Assert.Contains($"href=\"{destination}\"", html);
        Assert.True(html.IndexOf("My profile", StringComparison.Ordinal) < html.IndexOf(label, StringComparison.Ordinal));
    }

    [Fact]
    public async Task CustomerMenuListsNotificationsBetweenTransactionsAndProfile()
    {
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.AddAuthentication("MenuTest")
                .AddScheme<AuthenticationSchemeOptions, MenuAuthenticationHandler>("MenuTest", _ => { })));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Menu-Role", "Customer");
        var html = await client.GetStringAsync("/");
        Assert.Contains("href=\"/notifications\"", html);
        Assert.DoesNotContain("Admin workspace", html);
        Assert.True(html.IndexOf("href=\"/transactions\"", StringComparison.Ordinal) < html.IndexOf("href=\"/notifications\"", StringComparison.Ordinal));
        Assert.True(html.IndexOf("href=\"/notifications\"", StringComparison.Ordinal) < html.IndexOf("href=\"/my-profile\"", StringComparison.Ordinal));
    }

    [Fact, Trait("Category", "Integration")]
    public async Task CustomerInboxOnlyShowsOwnedEventsAndPersistsReadState()
    {
        var database = "test_inbox_" + Guid.NewGuid().ToString("N");
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.UseEnvironment("Testing")
            .ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MongoDb:ConnectionString"] = "mongodb://localhost:27017/?replicaSet=rs0",
                ["MongoDb:ReplicaSetName"] = "rs0",
                ["MongoDb:DatabaseName"] = database
            })).ConfigureTestServices(services => services.AddAuthentication("MenuTest")
                .AddScheme<AuthenticationSchemeOptions, MenuAuthenticationHandler>("MenuTest", _ => { })));
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("X-Menu-Role", "Customer");
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IMongoDbContext>();
        var records = context.Database.GetCollection<ReservationRecord>("reservations");
        var time = new DateTime(2026, 11, 15, 3, 0, 0, DateTimeKind.Utc);
        var owned = new ReservationRecord
        {
            AccountId = "menu-test",
            Reference = "OWNED-INBOX",
            History = [
            new() { EventType = "SubmissionCreated", AtUtc = time },
            new() { EventType = "ReservationConfirmed", AtUtc = time.AddMinutes(1) },
            new() { EventType = "AttendanceCorrected", AtUtc = time.AddMinutes(2), Reason = "Private staff note" }
        ]
        };
        var other = new ReservationRecord { AccountId = "someone-else", Reference = "PRIVATE-INBOX", History = [new() { EventType = "ReservationConfirmed", AtUtc = time }] };
        try
        {
            Assert.Contains("No notifications yet", await client.GetStringAsync("/notifications"));
            Assert.Equal("{\"unread\":false}", await client.GetStringAsync("/notifications/unread"));
            await records.InsertManyAsync([owned, other]);
            Assert.Equal("{\"unread\":true}", await client.GetStringAsync("/notifications/unread"));
            var page = await client.GetStringAsync("/notifications");
            Assert.Contains("Reservation confirmed", page);
            Assert.Contains("Request received", page);
            Assert.Contains("OWNED-INBOX", page);
            Assert.DoesNotContain("PRIVATE-INBOX", page);
            Assert.DoesNotContain("Private staff note", page);
            Assert.DoesNotContain("data-customer-unread hidden", page);
            var csrf = WebUtility.HtmlDecode(Regex.Match(page, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
            async Task<string> ReadAsync(string id, int index, DateTime at)
            {
                using var response = await client.PostAsync("/notifications/read-visible", new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["__RequestVerificationToken"] = csrf,
                    ["rows[0].Id"] = $"{id}:{index}",
                    ["rows[0].ActivityTicks"] = at.Ticks.ToString()
                }));
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsStringAsync();
            }

            Assert.Equal("{\"unread\":1}", await ReadAsync(other.Id, 0, time));
            Assert.Null((await records.Find(x => x.Id == other.Id).SingleAsync()).CustomerReadHistory);
            Assert.Equal("{\"unread\":1}", await ReadAsync(owned.Id, 1, time.AddMinutes(1)));
            Assert.Equal("{\"unread\":0}", await ReadAsync(owned.Id, 0, time));
            Assert.Equal("{\"unread\":false}", await client.GetStringAsync("/notifications/unread"));
            var refreshed = await client.GetStringAsync("/notifications");
            Assert.DoesNotMatch("data-unread-id=\"[^\"]+\"", refreshed);
            Assert.Contains("data-customer-unread hidden", refreshed);
        }
        finally { await context.Client.DropDatabaseAsync(database); }
    }

    private sealed class MenuAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(
            AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, "menu-test"), new Claim(ClaimTypes.Role, Request.Headers["X-Menu-Role"].FirstOrDefault() ?? "Admin")], Scheme.Name)), Scheme.Name)));
    }
}
