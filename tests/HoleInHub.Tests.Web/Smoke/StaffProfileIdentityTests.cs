using System.Net;
using System.Text.RegularExpressions;
using HoleInHub.Data.Context;
using HoleInHub.Data.Migrations.Definitions;
using HoleInHub.Web.Accounts;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HoleInHub.Tests.Web.Smoke;

[Trait("Category", "Integration")]
public sealed class StaffProfileIdentityTests
{
    [Fact]
    public async Task OwnProfileCannotChangeRoleAndPasswordChangeRequiresCurrentPasswordAndEndsSession()
    {
        var database = "test_staff_profile_" + Guid.NewGuid().ToString("N");
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseEnvironment("Testing").ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MongoDb:ConnectionString"] = "mongodb://localhost:27017/?replicaSet=rs0",
                ["MongoDb:DatabaseName"] = database,
                ["MongoDb:ReplicaSetName"] = "rs0",
                ["Smtp:Host"] = ""
            })).ConfigureTestServices(services => services.AddDataProtection().UseEphemeralDataProtectionProvider()));
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IMongoDbContext>();
        try
        {
            await new AccountsMigration().UpAsync(context.Database, default);
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AccountUser>>();
            var user = new AccountUser { Email = "profile-admin@example.test", FirstName = "Original", LastName = "Admin", Phone = "09123456789", EmailConfirmed = true, Roles = ["Admin"] };
            Assert.True((await users.CreateAsync(user, "OriginalPassword123")).Succeeded);
            user.Roles = ["Admin"];
            Assert.True((await users.UpdateAsync(user)).Succeeded);
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
            var login = await client.GetStringAsync("/account/sign-in");
            Assert.Equal(HttpStatusCode.Redirect, (await client.PostAsync("/account/sign-in", Form(login, new() { ["Email"] = user.Email, ["Password"] = "OriginalPassword123" }))).StatusCode);
            var profile = await client.GetStringAsync("/staff/profile");
            Assert.Contains("Original Admin", profile);
            Assert.Contains("staff-user-menu", profile);
            Assert.DoesNotContain("Change PIN", profile);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/staff/profile", new FormUrlEncodedContent([]))).StatusCode);
            var saved = await client.PostAsync("/staff/profile", Form(profile, new()
            {
                ["FirstName"] = "Updated",
                ["LastName"] = "Admin",
                ["Phone"] = "+821012345678",
                ["Id"] = "someone-else",
                ["Email"] = "different@example.test",
                ["Roles"] = "Customer"
            }));
            Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);
            var current = (await users.FindByIdAsync(user.Id))!;
            Assert.Equal("Updated", current.FirstName);
            Assert.Equal(user.Email, current.Email);
            Assert.Equal(new[] { "Admin" }, current.Roles);
            var change = new Dictionary<string, string> { ["CurrentPassword"] = "WrongPassword123", ["Password"] = "UpdatedPassword123", ["ConfirmPassword"] = "UpdatedPassword123" };
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/staff/profile/password", Form(profile, change))).StatusCode);
            Assert.Equal(user.SecurityStamp, (await users.FindByIdAsync(user.Id))!.SecurityStamp);
            change["CurrentPassword"] = "OriginalPassword123";
            Assert.Equal(HttpStatusCode.Redirect, (await client.PostAsync("/staff/profile/password", Form(profile, change))).StatusCode);
            var changed = (await users.FindByIdAsync(user.Id))!;
            Assert.NotEqual(user.SecurityStamp, changed.SecurityStamp);
            Assert.True(await users.CheckPasswordAsync(changed, "UpdatedPassword123"));
            Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/staff/profile")).StatusCode);
        }
        finally { await context.Client.DropDatabaseAsync(database); }
    }

    private static FormUrlEncodedContent Form(string html, Dictionary<string, string> fields)
    {
        fields["__RequestVerificationToken"] = WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
        return new(fields);
    }
}
