using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.RateLimiting;
using HoleInHub.Data.Context;
using HoleInHub.Data.Migrations.Definitions;
using HoleInHub.Data.Reservations;
using HoleInHub.Web.Accounts;
using HoleInHub.Web.ViewModels;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Xunit;

namespace HoleInHub.Tests.Web.Smoke;

public sealed class AuthenticationFlowTests
{
    [Theory]
    [InlineData("Marc", "Del Mundo", "Welcome back, Marc Del Mundo")]
    [InlineData("", "", "Welcome back")]
    public void WelcomeUsesTheSavedAccountName(string first, string last, string expected) =>
        Assert.Equal(expected, new AccountUser { FirstName = first, LastName = last }.WelcomeTitle);

    [Theory]
    [InlineData("abcdefgh", true)]
    [InlineData("12345678", true)]
    [InlineData("Abcdef12", true)]
    [InlineData("short12", false)]
    [InlineData("Password123!", false)]
    [InlineData("Password 123", false)]
    public void NewPasswordsAcceptAtLeastEightLettersOrNumbers(string password, bool valid)
    {
        var model = new ResetPasswordModel { UserId = "code", Token = "code", Password = password, ConfirmPassword = password };
        Assert.Equal(valid, Validator.TryValidateObject(model, new ValidationContext(model), [], true));
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static string Csrf(string html) => WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
    private static FormUrlEncodedContent Form(string html, Dictionary<string, string> values)
    {
        values["__RequestVerificationToken"] = Csrf(html);
        return new(values);
    }

    [Fact, Trait("Category", "Integration")]
    public async Task PasswordResetCodesAreBrowserBoundExpireAndInvalidateOldCredentials()
    {
        var database = "hih_auth_" + Guid.NewGuid().ToString("N");
        var clock = new Clock();
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.UseEnvironment("Testing")
            .UseSetting("Authentication:Google:Enabled", "false").ConfigureLogging(log => log.ClearProviders())
            .ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MongoDb:ConnectionString"] = "mongodb://localhost:27017/?replicaSet=rs0",
                ["MongoDb:ReplicaSetName"] = "rs0",
                ["MongoDb:DatabaseName"] = database,
                ["Smtp:Host"] = ""
            })).ConfigureServices(services =>
            {
                services.AddSingleton<TimeProvider>(clock);
                services.Configure<SecurityStampValidatorOptions>(options => options.ValidationInterval = TimeSpan.Zero);
                // This multi-browser flow tests code quotas; HTTP request throttling has a separate budget.
                services.RemoveAll<IConfigureOptions<RateLimiterOptions>>();
                services.AddRateLimiter(options => options.AddPolicy("accounts", _ =>
                    RateLimitPartition.GetFixedWindowLimiter("reset-flow", _ => new FixedWindowRateLimiterOptions
                    { PermitLimit = 100, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 })));
            }));
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IMongoDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AccountUser>>();
        HttpClient Client() => factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        using var client = Client();
        try
        {
            await new Migration_002_Reservations().UpAsync(context.Database, default);
            await new AccountsMigration().UpAsync(context.Database, default);
            await new Migration_004_BookingProcess().UpAsync(context.Database, default);
            await new Migration_005_GoogleAccounts().UpAsync(context.Database, default);
            await new Migration_006_SchemaHardening().UpAsync(context.Database, default);
            await new Migration_007_GuestReservationAccess().UpAsync(context.Database, default);
            var user = new AccountUser { Email = "reset@example.test", EmailConfirmed = true };
            Assert.True((await users.CreateAsync(user, "OldPassword123!")).Succeeded);
            var signInPage = await client.GetStringAsync("/account/sign-in");
            var signedIn = await client.PostAsync("/account/sign-in", Form(signInPage, new() { ["Email"] = user.Email, ["Password"] = "OldPassword123!", ["RememberMe"] = "true" }));
            Assert.Equal(HttpStatusCode.Redirect, signedIn.StatusCode);
            Assert.Contains(signedIn.Headers.GetValues("Set-Cookie"), cookie => cookie.Contains("Identity.Application") && cookie.Contains("expires=", StringComparison.OrdinalIgnoreCase));
            using var resetting = Client();
            var emailPage = await resetting.GetStringAsync("/account/forgot-password");
            var sent = await resetting.PostAsync("/account/forgot-password", Form(emailPage, new() { ["Email"] = "RESET@example.test" }));
            Assert.Equal("/account/reset-password/verify", sent.Headers.Location!.ToString());
            var page = await resetting.GetStringAsync(sent.Headers.Location);
            var mail = await context.Database.GetCollection<OutboxRecord>("outbox").Find(x => x.Recipient == user.Email).FirstAsync();
            var body = scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>().CreateProtector("BookingEmail").Unprotect(mail.Body[10..]);
            Assert.DoesNotContain("/account/reset-password?", body);
            var code = Regex.Match(body, "[0-9]{6}").Value;
            using var stranger = Client();
            Assert.Equal("/account/forgot-password", (await stranger.GetAsync("/account/reset-password/verify")).Headers.Location!.ToString());
            var wrong = await resetting.PostAsync("/account/reset-password/verify", Form(page, new() { ["code"] = "000000" }));
            Assert.Contains("invalid or expired", await wrong.Content.ReadAsStringAsync());
            var throttled = await resetting.PostAsync("/account/reset-password/verify", Form(page, new() { ["intent"] = "resend" }));
            Assert.Contains("Please wait", await throttled.Content.ReadAsStringAsync());
            var verified = await resetting.PostAsync("/account/reset-password/verify", Form(page, new() { ["code"] = code }));
            Assert.Equal("/account/reset-password", verified.Headers.Location!.ToString());
            var resetPage = await resetting.GetStringAsync(verified.Headers.Location);
            Assert.Contains("value=\"code\"", resetPage);
            var reset = await resetting.PostAsync("/account/reset-password", Form(resetPage, new()
            {
                ["UserId"] = "code",
                ["Token"] = "code",
                ["Password"] = "NewPass123",
                ["ConfirmPassword"] = "NewPass123"
            }));
            Assert.Equal(HttpStatusCode.Redirect, reset.StatusCode);
            var updated = (await users.FindByIdAsync(user.Id))!;
            Assert.True(await users.CheckPasswordAsync(updated, "NewPass123"));
            Assert.False(await users.CheckPasswordAsync(updated, "OldPassword123!"));
            Assert.Equal("/account/forgot-password", (await resetting.GetAsync("/account/reset-password")).Headers.Location!.ToString());

            clock.Now = clock.Now.AddMinutes(31);
            Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/my-profile")).StatusCode);

            // Unknown accounts reach the same code screen and never enqueue email.
            using var unknown = Client();
            var unknownPage = await unknown.GetStringAsync("/account/forgot-password");
            var unknownSent = await unknown.PostAsync("/account/forgot-password", Form(unknownPage, new() { ["Email"] = "unknown@example.test" }));
            Assert.Equal(sent.Headers.Location, unknownSent.Headers.Location);
            Assert.Equal(1, await context.Database.GetCollection<OutboxRecord>("outbox").CountDocumentsAsync(FilterDefinition<OutboxRecord>.Empty));
        }
        finally { await context.Client.DropDatabaseAsync(database); }
    }
}
