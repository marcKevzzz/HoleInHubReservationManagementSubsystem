using System.Net;
using System.Text.RegularExpressions;
using HoleInHub.Business.Reservations;
using HoleInHub.Data.Context;
using HoleInHub.Data.Migrations.Definitions;
using HoleInHub.Web.Accounts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace HoleInHub.Tests.Web.Smoke;

[Trait("Category", "Integration")]
public sealed class AccountIdentityTests
{
    [Fact]
    public async Task VerificationResetLockoutAndRolesUsePersistedIdentity()
    {
        var databaseName = $"holeinhub_accounts_test_{Guid.NewGuid():N}";
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseEnvironment("Testing").ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MongoDb:ConnectionString"] = "mongodb://localhost:27017/?replicaSet=rs0",
                ["MongoDb:DatabaseName"] = databaseName,
                ["MongoDb:ReplicaSetName"] = "rs0"
            })));
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IMongoDbContext>();
        try
        {
            await new AccountsMigration().UpAsync(context.Database, CancellationToken.None);
            await new Migration_002_Reservations().UpAsync(context.Database, CancellationToken.None);
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AccountUser>>();
            var user = new AccountUser { Email = "player@example.com", Roles = ["Administrator"] };
            Assert.True((await users.CreateAsync(user, "GoodPassword123!")).Succeeded);
            Assert.Equal(["Customer"], await users.GetRolesAsync(user));
            Assert.False(await users.IsEmailConfirmedAsync(user));
            Assert.True(await users.CheckPasswordAsync(user, "GoodPassword123!"));
            Assert.False(await users.CheckPasswordAsync(user, "IncorrectPassword!"));
            Assert.True(await scope.ServiceProvider.GetRequiredService<SignInManager<AccountUser>>().CanSignInAsync(user));
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
            var signInPage = await client.GetStringAsync("/account/sign-in");
            var csrf = WebUtility.HtmlDecode(Regex.Match(signInPage, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
            Assert.NotEmpty(csrf);
            var signedIn = await client.PostAsync("/account/sign-in", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = csrf,
                ["Email"] = user.Email,
                ["Password"] = "GoodPassword123!",
                ["ReturnUrl"] = "/Reservations/Create"
            }));
            Assert.Equal(HttpStatusCode.Redirect, signedIn.StatusCode);
            var booking = await client.GetStringAsync("/Reservations/Create");
            Assert.Contains("id=\"booking-verify-required\"", booking);
            Assert.DoesNotContain("id=\"booking-auth-dialog\"", booking);
            Assert.DoesNotContain("id=\"booking-submit\"", booking);
            csrf = WebUtility.HtmlDecode(Regex.Match(booking, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
            var blocked = await client.PostAsync("/Reservations/Create", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = csrf,
                ["Service"] = "Regular",
                ["Date"] = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)).ToString("yyyy-MM-dd"),
                ["StartTime"] = "11:00",
                ["DurationMinutes"] = "60",
                ["ResourceIds"] = "bay-1",
                ["FirstName"] = "Test",
                ["LastName"] = "Player",
                ["Phone"] = "09000000000",
                ["Attendees"] = "1",
                ["IdempotencyKey"] = Guid.NewGuid().ToString("N")
            }));
            Assert.Equal(HttpStatusCode.OK, blocked.StatusCode);
            Assert.Contains("Verify your email before submitting a reservation.", await blocked.Content.ReadAsStringAsync());
            Assert.Empty(await scope.ServiceProvider.GetRequiredService<ReservationService>().GetOwnedAsync(user.Id, CancellationToken.None));
            var tokenOptions = scope.ServiceProvider.GetRequiredService<IOptions<DataProtectionTokenProviderOptions>>().Value;
            tokenOptions.TokenLifespan = TimeSpan.FromSeconds(-1);
            var expiredToken = await users.GenerateEmailConfirmationTokenAsync(user);
            Assert.False((await users.ConfirmEmailAsync(user, expiredToken)).Succeeded);
            tokenOptions.TokenLifespan = TimeSpan.FromHours(2);
            var token = await users.GenerateEmailConfirmationTokenAsync(user);
            Assert.False((await users.ConfirmEmailAsync(user, token + "invalid")).Succeeded);
            var encoded = Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(System.Text.Encoding.UTF8.GetBytes(token));
            var verified = await client.GetStringAsync($"/account/verify-email?userId={user.Id}&token={encoded}");
            Assert.Contains("Your email is verified.", verified);
            Assert.Contains("id=\"booking-submit\"", await client.GetStringAsync("/Reservations/Create"));
            user = (await users.FindByIdAsync(user.Id))!;
            var principal = await scope.ServiceProvider.GetRequiredService<IUserClaimsPrincipalFactory<AccountUser>>().CreateAsync(user);
            Assert.True(principal.HasClaim("email_verified", "true"));
            Assert.True(principal.IsInRole("Customer"));
            Assert.False(principal.IsInRole("Administrator"));
            var reset = await users.GeneratePasswordResetTokenAsync(user);
            Assert.True((await users.ResetPasswordAsync(user, reset, "DifferentPassword456!")).Succeeded);
            Assert.False((await users.ResetPasswordAsync(user, reset, "ThirdPassword789!")).Succeeded);
            Assert.False(await users.CheckPasswordAsync(user, "GoodPassword123!"));
            Assert.True(await users.CheckPasswordAsync(user, "DifferentPassword456!"));
            for (var i = 0; i < 5; i++) { Assert.True((await users.AccessFailedAsync(user)).Succeeded); }
            Assert.True(await users.IsLockedOutAsync(user));
            var restored = await users.FindByEmailAsync("PLAYER@example.com");
            Assert.NotNull(restored);
            Assert.True(restored.EmailConfirmed);
            Assert.True(await users.IsLockedOutAsync(restored));
            Assert.False((await users.CreateAsync(new AccountUser { Email = "player@example.com" }, "GoodPassword123!")).Succeeded);
        }
        finally { await context.Client.DropDatabaseAsync(databaseName); }
    }
}
