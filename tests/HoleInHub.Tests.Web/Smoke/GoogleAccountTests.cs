using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.RegularExpressions;
using HoleInHub.Data.Context;
using HoleInHub.Data.Migrations.Definitions;
using HoleInHub.Data.Reservations;
using HoleInHub.Web.Accounts;
using HoleInHub.Web.Controllers;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using Xunit;

namespace HoleInHub.Tests.Web.Smoke;

public sealed class GoogleEmailTrustTests
{
    [Fact]
    public void EnabledGoogleRequiresBothCredentials()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.UseEnvironment("Testing").UseSetting("Authentication:Google:Enabled", "true").UseSetting("Authentication:Google:ClientId", "test-client").UseSetting("Authentication:Google:ClientSecret", ""));
        var error = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
        Assert.Contains("requires both ClientId and ClientSecret", error.Message);
    }
    [Theory]
    [InlineData("person@gmail.com", "true", "", true)]
    [InlineData("person@company.test", "true", "company.test", true)]
    [InlineData("person@company.test", "true", "", true)]
    [InlineData("person@gmail.com", "false", "", false)]
    public void OnlyAuthoritativeVerifiedGoogleEmailsSkipLocalVerification(string email, string verified, string domain, bool expected)
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Email, email), new Claim("google:email_verified", verified), new Claim("google:hosted_domain", domain)]));
        Assert.Equal(expected, AccountController.GoogleEmailIsAuthoritative(principal));
    }
}

[Trait("Category", "Integration")]
public sealed class GoogleAccountTests
{
    private sealed class GoogleBackchannel : HttpMessageHandler
    {
        public string Subject { get; set; } = "google-subject-123";
        public string Email { get; set; } = "google-player@gmail.com";
        public bool Verified { get; set; } = true;
        public string HostedDomain { get; set; } = "";
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var json = request.RequestUri!.AbsolutePath.Contains("token", StringComparison.Ordinal)
                ? "{\"access_token\":\"test-access-token\",\"token_type\":\"Bearer\",\"expires_in\":300}"
                : System.Text.Json.JsonSerializer.Serialize(new { sub = Subject, email = Email, email_verified = Verified, hd = HostedDomain, given_name = "Google", family_name = "Player" });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
        }
    }
    private static WebApplicationFactory<Program> Factory(string database, GoogleBackchannel backchannel) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.UseEnvironment("Testing")
            .UseSetting("Authentication:Google:Enabled", "true").UseSetting("Authentication:Google:ClientId", "local-test-client").UseSetting("Authentication:Google:ClientSecret", "local-test-secret")
            .ConfigureLogging(log => log.ClearProviders())
            .ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MongoDb:ConnectionString"] = "mongodb://localhost:27017/?replicaSet=rs0",
                ["MongoDb:ReplicaSetName"] = "rs0",
                ["MongoDb:DatabaseName"] = database,
                ["Authentication:Google:Enabled"] = "true",
                ["Authentication:Google:ClientId"] = "local-test-client",
                ["Authentication:Google:ClientSecret"] = "local-test-secret",
                ["AccountEmail:PublicBaseUrl"] = "https://localhost",
                ["Smtp:Host"] = ""
            })).ConfigureServices(services => services.PostConfigure<GoogleOptions>("Google", options => options.Backchannel = new HttpClient(backchannel))));
    private static string Csrf(string html) => WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
    private static async Task Seed(IMongoDbContext context)
    {
        await new Migration_002_Reservations().UpAsync(context.Database, default);
        await new AccountsMigration().UpAsync(context.Database, default);
        await new Migration_004_BookingProcess().UpAsync(context.Database, default);
        await new Migration_005_GoogleAccounts().UpAsync(context.Database, default);
        await new Migration_006_SchemaHardening().UpAsync(context.Database, default);
    }
    private static async Task<HttpResponseMessage> GoogleLogin(HttpClient client, string returnUrl = "/Reservations/Create")
    {
        var page = await client.GetStringAsync("/account/sign-in"); Assert.Contains("Continue with Google", page);
        var challenge = await client.PostAsync("/account/google", new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = Csrf(page), ["returnUrl"] = returnUrl }));
        Assert.Equal(HttpStatusCode.Redirect, challenge.StatusCode);
        var state = QueryHelpers.ParseQuery(challenge.Headers.Location!.Query)["state"].ToString(); Assert.NotEmpty(state);
        var callback = await client.GetAsync("/signin-google?code=local-code&state=" + Uri.EscapeDataString(state));
        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        return await client.GetAsync(callback.Headers.Location);
    }

    [Fact]
    public async Task GoogleCreatesOneCustomerAndPrefillsContactsWithoutLinkingExistingEmailOrBypassingLockout()
    {
        var database = "hih_google_" + Guid.NewGuid().ToString("N"); var backchannel = new GoogleBackchannel();
        await using var factory = Factory(database, backchannel); using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IMongoDbContext>(); var users = scope.ServiceProvider.GetRequiredService<UserManager<AccountUser>>();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        try
        {
            await Seed(context);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/account/google", new FormUrlEncodedContent(new Dictionary<string, string>()))).StatusCode);
            var completion = await GoogleLogin(client, "https://external.example/steal"); Assert.Equal(HttpStatusCode.OK, completion.StatusCode);
            var form = await completion.Content.ReadAsStringAsync(); Assert.Contains("Complete your account", form); Assert.Contains("value=\"Google\"", form);
            var invalidContact = await client.PostAsync("/account/google-complete", new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = Csrf(form), ["FirstName"] = "Google", ["LastName"] = "Player", ["Phone"] = "1------" }));
            Assert.Equal(HttpStatusCode.OK, invalidContact.StatusCode); Assert.Contains("valid contact number", await invalidContact.Content.ReadAsStringAsync()); Assert.Null(await users.FindByEmailAsync(backchannel.Email));
            var created = await client.PostAsync("/account/google-complete", new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = Csrf(form), ["FirstName"] = " Saved ", ["LastName"] = " Player ", ["Phone"] = "09123456789", ["Email"] = "attacker@example.test", ["Roles"] = "Administrator", ["ReturnUrl"] = "/Reservations/Create" }));
            Assert.Equal("/Reservations/Create", created.Headers.Location!.ToString());
            var user = (await users.FindByEmailAsync(backchannel.Email))!; Assert.NotNull(user); Assert.True(user.EmailConfirmed); Assert.False(await users.HasPasswordAsync(user)); Assert.Equal(["Customer"], user.Roles); Assert.Equal("Saved", user.FirstName); Assert.Equal(backchannel.Subject, user.GoogleSubject);
            Assert.Null(await users.FindByEmailAsync("attacker@example.test"));
            await context.Database.GetCollection<ReservationRecord>("reservations").InsertOneAsync(new ReservationRecord { AccountId = user.Id, Email = user.Email, Reference = "OLD-CONTACT", Service = "Regular", ResourceIds = ["bay-2"], Attendees = 1, CreatedUtc = DateTime.UtcNow, ReviewDeadlineUtc = DateTime.UtcNow.AddMinutes(30), FirstName = "Old", LastName = "Contact", Phone = "09000000000", IdempotencyKey = "old-contact", SubmissionHash = "old", StartUtc = DateTime.UtcNow, EndUtc = DateTime.UtcNow.AddHours(1) });
            var booking = await client.GetStringAsync("/Reservations/Create"); Assert.Contains("value=\"Saved\"", booking); Assert.Contains("value=\"09123456789\"", booking); Assert.DoesNotContain("value=\"Old\"", booking);
            user.LastName = ""; Assert.True((await users.UpdateAsync(user)).Succeeded);
            var mixedContact = await client.GetStringAsync("/Reservations/Create"); Assert.Contains("value=\"Saved\"", mixedContact); Assert.Contains("value=\"Contact\"", mixedContact); Assert.Contains("value=\"09123456789\"", mixedContact);
            user.EmailConfirmed = false; Assert.True((await users.UpdateAsync(user)).Succeeded);
            using var second = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
            var repeated = await GoogleLogin(second); Assert.Equal("/Reservations/Create", repeated.Headers.Location!.ToString());
            Assert.Equal(1, await context.Database.GetCollection<HoleInHub.Data.Accounts.AccountRecord>("accounts").CountDocumentsAsync(FilterDefinition<HoleInHub.Data.Accounts.AccountRecord>.Empty));
            user = (await users.FindByIdAsync(user.Id))!; Assert.True(user.EmailConfirmed);
            Assert.True((await users.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.AddMinutes(15))).Succeeded);
            await using var retryFactory = Factory(database, backchannel);
            using var retryClient = retryFactory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
            var locked = await GoogleLogin(retryClient); Assert.Equal("/account/sign-in", locked.Headers.Location!.ToString());
            backchannel.Subject = "different-google-subject";
            var duplicate = await GoogleLogin(retryClient); Assert.StartsWith("/account/sign-in?returnUrl=", duplicate.Headers.Location!.ToString());
            Assert.Contains("not linked automatically", await retryClient.GetStringAsync(duplicate.Headers.Location));
            Assert.Equal(backchannel.Email, (await users.FindByIdAsync(user.Id))!.Email);
        }
        finally { await context.Client.DropDatabaseAsync(database); }
    }

    [Fact]
    public async Task RegistrationValidatesAndPersistsContactAndMigrationPreservesLegacyAccounts()
    {
        var database = "hih_signup_" + Guid.NewGuid().ToString("N"); await using var factory = Factory(database, new GoogleBackchannel());
        using var scope = factory.Services.CreateScope(); var context = scope.ServiceProvider.GetRequiredService<IMongoDbContext>(); var users = scope.ServiceProvider.GetRequiredService<UserManager<AccountUser>>();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        try
        {
            await Seed(context); await new Migration_005_GoogleAccounts().UpAsync(context.Database, default);
            Assert.True((await users.CreateAsync(new AccountUser { Email = "legacy@example.test" }, "TestingPassword123!")).Succeeded);
            var page = await client.GetStringAsync("/account/sign-up"); var form = new Dictionary<string, string> { ["__RequestVerificationToken"] = Csrf(page), ["Email"] = "new@example.test", ["Password"] = "TestingPassword123!", ["ConfirmPassword"] = "TestingPassword123!" };
            var invalid = await client.PostAsync("/account/sign-up", new FormUrlEncodedContent(form)); Assert.Equal(HttpStatusCode.OK, invalid.StatusCode); Assert.Null(await users.FindByEmailAsync("new@example.test"));
            form["FirstName"] = "First"; form["LastName"] = "Last"; form["Phone"] = "not a number";
            Assert.Contains("valid contact number", await (await client.PostAsync("/account/sign-up", new FormUrlEncodedContent(form))).Content.ReadAsStringAsync());
            form["Phone"] = "09123456789"; form["ReturnUrl"] = "/Reservations/Create";
            Assert.Equal(HttpStatusCode.Redirect, (await client.PostAsync("/account/sign-up", new FormUrlEncodedContent(form))).StatusCode);
            var user = (await users.FindByEmailAsync("new@example.test"))!; Assert.Equal("First", user.FirstName); Assert.Equal("Last", user.LastName); Assert.Equal(form["Phone"], user.Phone); Assert.False(user.EmailConfirmed);
            user.GoogleSubject = "unique-subject"; Assert.True((await users.UpdateAsync(user)).Succeeded);
            Assert.False((await users.CreateAsync(new AccountUser { Email = "duplicate-subject@example.test", GoogleSubject = "unique-subject" })).Succeeded);
            var legacy = (await users.FindByEmailAsync("legacy@example.test"))!; Assert.Equal("", legacy.FirstName); Assert.Null(legacy.GoogleSubject);
            Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/account/google-callback")).StatusCode);
            var failed = await client.GetAsync("/signin-google?code=bad&state=invalid"); Assert.Equal(HttpStatusCode.Redirect, failed.StatusCode); Assert.Equal("/account/google-failed", failed.Headers.Location!.ToString());
        }
        finally { await context.Client.DropDatabaseAsync(database); }
    }

    [Theory]
    [InlineData("player@external.test", true, "", true)]
    [InlineData("player@gmail.com", false, "", false)]
    [InlineData("player@workspace.test", true, "workspace.test", true)]
    public async Task GoogleEmailVerificationUsesMappedProviderClaims(string email, bool verified, string domain, bool confirmed)
    {
        var database = "hih_google_verify_" + Guid.NewGuid().ToString("N"); var backchannel = new GoogleBackchannel { Email = email, Verified = verified, HostedDomain = domain }; await using var factory = Factory(database, backchannel);
        using var scope = factory.Services.CreateScope(); var context = scope.ServiceProvider.GetRequiredService<IMongoDbContext>();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        try
        {
            await Seed(context); var completion = await GoogleLogin(client); if (!verified) { Assert.Equal(HttpStatusCode.Redirect, completion.StatusCode); Assert.Null(await scope.ServiceProvider.GetRequiredService<UserManager<AccountUser>>().FindByEmailAsync(email)); return; }
            var html = await completion.Content.ReadAsStringAsync();
            var form = new Dictionary<string, string> { ["__RequestVerificationToken"] = Csrf(html), ["FirstName"] = "Google", ["LastName"] = "Player", ["Phone"] = "09123456789", ["ReturnUrl"] = "/Reservations/Create" };
            var created = await client.PostAsync("/account/google-complete", new FormUrlEncodedContent(form));
            Assert.StartsWith(confirmed ? "/Reservations/Create" : "/account/verify-email", created.Headers.Location!.ToString());
            var user = (await scope.ServiceProvider.GetRequiredService<UserManager<AccountUser>>().FindByEmailAsync(backchannel.Email))!;
            Assert.Equal(confirmed, user.EmailConfirmed); Assert.Equal("Google", user.FirstName);
            Assert.Contains(confirmed ? "id=\"booking-submit\"" : "id=\"booking-verify-required\"", await client.GetStringAsync("/Reservations/Create"));
            Assert.Equal(confirmed ? 0 : 1, await context.Database.GetCollection<OutboxRecord>("outbox").CountDocumentsAsync(x => x.Recipient == backchannel.Email));
        }
        finally { await context.Client.DropDatabaseAsync(database); }
    }
}
