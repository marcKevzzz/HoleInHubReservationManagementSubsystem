using System.Net;
using System.Text.RegularExpressions;
using HoleInHub.Business.Accounts;
using HoleInHub.Business.Reservations;
using HoleInHub.Data.Context;
using HoleInHub.Data.Migrations.Definitions;
using HoleInHub.Data.Reservations;
using HoleInHub.Web.Accounts;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using Xunit;

namespace HoleInHub.Tests.Web.Smoke;

[Trait("Category", "Integration")]
public sealed class AccountVerificationTests
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(DateTime.UtcNow.Date.AddDays(1).AddHours(4), TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private static WebApplicationFactory<Program> Factory(string database, Clock clock) => new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.UseEnvironment("Testing").UseSetting("Authentication:Google:Enabled", "false")
        .ConfigureLogging(log => log.ClearProviders()).ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["MongoDb:ConnectionString"] = "mongodb://localhost:27017/?replicaSet=rs0",
            ["MongoDb:ReplicaSetName"] = "rs0",
            ["MongoDb:DatabaseName"] = database,
            ["AccountEmail:PublicBaseUrl"] = "https://localhost",
            ["Smtp:Host"] = ""
        })).ConfigureServices(services => services.AddSingleton<TimeProvider>(clock)));
    private static HttpClient Client(WebApplicationFactory<Program> factory) => factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
    private static string Csrf(string html) => WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
    private static async Task Seed(IMongoDbContext context)
    {
        await new Migration_002_Reservations().UpAsync(context.Database, default); await new AccountsMigration().UpAsync(context.Database, default);
        await new Migration_004_BookingProcess().UpAsync(context.Database, default); await new Migration_005_GoogleAccounts().UpAsync(context.Database, default);
        await new Migration_006_SchemaHardening().UpAsync(context.Database, default);
    }
    private static async Task<string> SignUp(HttpClient client)
    {
        var page = await client.GetStringAsync("/account/sign-up?returnUrl=%2FReservations%2FCreate");
        var result = await client.PostAsync("/account/sign-up", new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = Csrf(page), ["Email"] = "verify@example.test", ["Password"] = "TestingPassword123!", ["ConfirmPassword"] = "TestingPassword123!", ["FirstName"] = "Verified", ["LastName"] = "Player", ["Phone"] = "09123456789", ["ReturnUrl"] = "/Reservations/Create" }));
        Assert.Equal(HttpStatusCode.Redirect, result.StatusCode);
        return await client.GetStringAsync(result.Headers.Location);
    }
    private static async Task<string> Code(IMongoDbContext context, IServiceProvider services)
    {
        var mail = await context.Database.GetCollection<OutboxRecord>("outbox").Find(x => x.Recipient == "verify@example.test").SortByDescending(x => x.DueUtc).FirstAsync();
        Assert.StartsWith("protected:", mail.Body);
        return Regex.Match(services.GetRequiredService<IDataProtectionProvider>().CreateProtector("BookingEmail").Unprotect(mail.Body[10..]), "[0-9]{6}").Value;
    }
    private static Task<HttpResponseMessage> Submit(HttpClient client, string html, string? code = null, string? intent = null) => client.PostAsync("/account/verify-email", new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = Csrf(html), ["code"] = code ?? "", ["intent"] = intent ?? "", ["Email"] = "attacker@example.test", ["ReturnUrl"] = "/Reservations/Create" }));

    [Fact]
    public async Task SignupUsesBoundEmailCodeAndVerifiedAccountSkipsBookingVerification()
    {
        var database = "hih_account_code_" + Guid.NewGuid().ToString("N"); var clock = new Clock(); await using var factory = Factory(database, clock);
        using var scope = factory.Services.CreateScope(); var context = scope.ServiceProvider.GetRequiredService<IMongoDbContext>(); var users = scope.ServiceProvider.GetRequiredService<UserManager<AccountUser>>(); using var client = Client(factory);
        try
        {
            await Seed(context); var page = await SignUp(client); Assert.Contains("verify@example.test", page); Assert.DoesNotContain("name=\"Email\"", page); Assert.Contains("autocomplete=\"one-time-code\"", page);
            using var stranger = Client(factory); var strangerPage = await stranger.GetStringAsync("/account/verify-email"); Assert.DoesNotContain("verify@example.test", strangerPage); Assert.DoesNotContain("one-time-code", strangerPage);
            var wrong = await Submit(client, page, "000000"); Assert.Contains("invalid or expired", await wrong.Content.ReadAsStringAsync());
            Assert.False((await users.FindByEmailAsync("verify@example.test"))!.EmailConfirmed);
            var throttled = await Submit(client, page, intent: "resend"); Assert.Contains("Wait one minute", await throttled.Content.ReadAsStringAsync());
            var success = await Submit(client, page, await Code(context, scope.ServiceProvider)); Assert.Equal(HttpStatusCode.Redirect, success.StatusCode);
            var complete = await client.GetStringAsync(success.Headers.Location); Assert.Equal("/", success.Headers.Location!.ToString()); Assert.DoesNotContain("one-time-code", complete);
            Assert.True((await users.FindByEmailAsync("verify@example.test"))!.EmailConfirmed); Assert.Null(await users.FindByEmailAsync("attacker@example.test"));
            var booking = await client.GetStringAsync("/Reservations/Create"); Assert.Contains("value=\"Verified\"", booking); Assert.Contains("readonly", booking); Assert.Contains("value=\"verify@example.test\"", booking); Assert.DoesNotContain("id=\"booking-verify-required\"", booking); Assert.Contains("id=\"booking-submit\"", booking);
            Assert.Equal(1, await context.Database.GetCollection<OutboxRecord>("outbox").CountDocumentsAsync(FilterDefinition<OutboxRecord>.Empty));
            Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/account/sign-up")).StatusCode);
            var extra = await client.PostAsync("/account/sign-up", new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = Csrf(booking), ["Email"] = "second@example.test" }));
            Assert.Equal(HttpStatusCode.Redirect, extra.StatusCode); Assert.Null(await users.FindByEmailAsync("second@example.test"));
        }
        finally { await context.Client.DropDatabaseAsync(database); }
    }

    [Fact]
    public async Task ExpiredAccountCodeCanBeResentWithoutEnteringEmailAgain()
    {
        var database = "hih_account_expiry_" + Guid.NewGuid().ToString("N"); var clock = new Clock(); await using var factory = Factory(database, clock);
        using var scope = factory.Services.CreateScope(); var context = scope.ServiceProvider.GetRequiredService<IMongoDbContext>(); using var client = Client(factory);
        try
        {
            await Seed(context); var page = await SignUp(client); var oldCode = await Code(context, scope.ServiceProvider); clock.Now = clock.Now.AddMinutes(11);
            Assert.Contains("expired", await (await Submit(client, page, oldCode)).Content.ReadAsStringAsync());
            var resent = await Submit(client, page, intent: "resend"); var nextPage = await resent.Content.ReadAsStringAsync(); Assert.DoesNotContain("name=\"Email\"", nextPage);
            var code = await Code(context, scope.ServiceProvider); Assert.Equal(HttpStatusCode.Redirect, (await Submit(client, nextPage, code)).StatusCode);
            Assert.True((await scope.ServiceProvider.GetRequiredService<UserManager<AccountUser>>().FindByEmailAsync("verify@example.test"))!.EmailConfirmed);
        }
        finally { await context.Client.DropDatabaseAsync(database); }
    }

    [Fact]
    public async Task SharedCoreKeepsPurposeScopesSeparateAndRejectsReuseAndAttemptExhaustion()
    {
        var database = "hih_code_scopes_" + Guid.NewGuid().ToString("N"); var clock = new Clock(); await using var factory = Factory(database, clock);
        using var scope = factory.Services.CreateScope(); var context = scope.ServiceProvider.GetRequiredService<IMongoDbContext>();
        try
        {
            await Seed(context); var core = scope.ServiceProvider.GetRequiredService<EmailVerificationService>(); var guest = scope.ServiceProvider.GetRequiredService<GuestBookingService>(); var email = "verify@example.test";
            await core.IssueAsync("account-email:session", email, EmailVerificationService.CodeHash("account-email:session", email, "123456"), "encrypted", "Account code", "AccountEmail", default);
            await guest.IssueAsync("session", email, GuestBookingService.CodeHash("session", email, "654321"), "encrypted", default, "draft");
            await Assert.ThrowsAsync<ReservationValidationException>(() => guest.VerifyAsync("session", email, "123456", default));
            await core.VerifyAsync("account-email:session", email, "123456", default);
            await Assert.ThrowsAsync<ReservationValidationException>(() => core.VerifyAsync("account-email:session", email, "123456", default));
            for (var i = 0; i < 4; i++) { await Assert.ThrowsAsync<ReservationValidationException>(() => guest.VerifyAsync("session", email, "000000", default)); }
            await Assert.ThrowsAsync<ReservationValidationException>(() => guest.VerifyAsync("session", email, "654321", default));
        }
        finally { await context.Client.DropDatabaseAsync(database); }
    }

    [Fact]
    public async Task LegacyEmailLinksStillConfirmAndRejectExpiredTokens()
    {
        var database = "hih_legacy_email_" + Guid.NewGuid().ToString("N"); await using var factory = Factory(database, new Clock()); using var scope = factory.Services.CreateScope(); var context = scope.ServiceProvider.GetRequiredService<IMongoDbContext>(); using var client = Client(factory);
        try
        {
            await Seed(context); var users = scope.ServiceProvider.GetRequiredService<UserManager<AccountUser>>(); var user = new AccountUser { Email = "legacy-link@example.test" }; Assert.True((await users.CreateAsync(user, "TestingPassword123!")).Succeeded);
            var options = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<DataProtectionTokenProviderOptions>>().Value;
            options.TokenLifespan = TimeSpan.FromSeconds(-1); var expired = await users.GenerateEmailConfirmationTokenAsync(user);
            string Link(string token) => "/account/verify-email?userId=" + user.Id + "&token=" + Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(System.Text.Encoding.UTF8.GetBytes(token));
            Assert.Contains("invalid or expired", await client.GetStringAsync(Link(expired))); Assert.False((await users.FindByIdAsync(user.Id))!.EmailConfirmed);
            options.TokenLifespan = TimeSpan.FromHours(2); var valid = await users.GenerateEmailConfirmationTokenAsync(user);
            Assert.Contains("Your email is verified.", await client.GetStringAsync(Link(valid))); Assert.True((await users.FindByIdAsync(user.Id))!.EmailConfirmed);
        }
        finally { await context.Client.DropDatabaseAsync(database); }
    }
}
