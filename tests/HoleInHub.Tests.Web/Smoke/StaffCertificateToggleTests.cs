using System.Net;
using HoleInHub.Business.Accounts;
using HoleInHub.Data.Context;
using HoleInHub.Data.Migrations.Definitions;
using HoleInHub.Web.Accounts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HoleInHub.Tests.Web.Smoke;

public sealed partial class StaffPinAccessTests
{
    [Theory]
    [InlineData(true, true, "staff.example.test", true, false)]
    [InlineData(false, true, "staff.example.test", true, true)]
    [InlineData(false, true, "staff.example.test", false, false)]
    [InlineData(false, true, "other.example.test", true, false)]
    [InlineData(false, false, "staff.example.test", true, false)]
    public async Task CertificateToggleRetainsHttpsHostAndEnabledChecks(bool requireCertificate, bool enabled,
        string host, bool https, bool allowed)
    {
        Assert.True(new StaffPinAccess().RequireDesktopCertificate);
        var access = new StaffPinAccess
        {
            Enabled = enabled,
            Host = "staff.example.test",
            RequireDesktopCertificate = requireCertificate
        };
        var context = new DefaultHttpContext();
        context.Request.Host = new HostString(host);
        context.Request.Scheme = https ? "https" : "http";
        Assert.Equal(allowed, await access.GetDesktopAsync(context, DateTimeOffset.UtcNow) is not null);
    }

    [Fact]
    public async Task CertificateFreeModeStartsInProductionWithoutEnrolledFingerprints()
    {
        using var certificate = DesktopCertificate(DateTimeOffset.UtcNow);
        using var factory = Factory(certificate, "test_certificate_free_start", requireDesktopCertificate: false)
            .WithWebHostBuilder(builder => builder.UseEnvironment("Production")
                .UseSetting("DataProtection:KeyDirectory", AppContext.BaseDirectory)
                .ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["StaffPin:DesktopFingerprints:0"] = null,
                    ["DataProtection:KeyDirectory"] = AppContext.BaseDirectory
                })));
        using var browser = Client(factory, false);
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/staff/sign-in")).StatusCode);
    }

    [Theory, Trait("Category", "Integration")]
    [InlineData(StaffRoles.Staff)]
    [InlineData(StaffRoles.Manager)]
    public async Task CertificateFreeStaffAndManagerSignInRetainsRoleAndPinRevocation(string role)
    {
        using var certificate = DesktopCertificate(DateTimeOffset.UtcNow);
        var database = "test_certificate_free_" + Guid.NewGuid().ToString("N");
        await using var factory = Factory(certificate, database, requireDesktopCertificate: false);
        using var browser = Client(factory, false);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IMongoDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AccountUser>>();
        var pins = scope.ServiceProvider.GetRequiredService<StaffPinService>();
        var access = scope.ServiceProvider.GetRequiredService<StaffPinAccess>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<AccountUser>>();
        try
        {
            await new AccountsMigration().UpAsync(context.Database, default);
            await new Migration_016_StaffPinAttempts().UpAsync(context.Database, default);
            var employee = new AccountUser { Email = "demo@example.test", EmailConfirmed = true, Roles = [role] };
            Assert.True((await users.CreateAsync(employee, "StaffTestPassword123!")).Succeeded);
            employee.Roles = [role];
            Assert.True((await users.UpdateAsync(employee)).Succeeded);
            Assert.True(await pins.TrySaveAsync(employee.Id, hasher.HashPassword(employee, access.PinInput(employee, "048217")),
                employee.SecurityStamp, default));
            using var wrong = await SubmitAsync(browser, "/staff/sign-in", new() { ["Pin"] = "048216" });
            Assert.Contains("That PIN didn", await wrong.Content.ReadAsStringAsync());
            using var signedIn = await SubmitAsync(browser, "/staff/sign-in", new() { ["Pin"] = "048217" });
            Assert.Equal(HttpStatusCode.Redirect, signedIn.StatusCode);
            Assert.Equal(role == StaffRoles.Manager ? "/staff/dashboard" : "/staff", signedIn.Headers.Location?.OriginalString);
            Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/staff/dashboard")).StatusCode);
            Assert.Equal(role == StaffRoles.Manager ? HttpStatusCode.OK : HttpStatusCode.Forbidden,
                (await browser.GetAsync("/staff/settings")).StatusCode);
            Assert.True(await pins.TrySaveAsync(employee.Id, hasher.HashPassword(employee, access.PinInput(employee, "582049")),
                employee.SecurityStamp, default));
            Assert.Equal(HttpStatusCode.Redirect, (await browser.GetAsync("/staff/dashboard")).StatusCode);
            using var again = await SubmitAsync(browser, "/staff/sign-in", new() { ["Pin"] = "582049" });
            Assert.Equal(HttpStatusCode.Redirect, again.StatusCode);
            employee = (await users.FindByIdAsync(employee.Id))!;
            employee.Roles = ["Customer"];
            Assert.True((await users.UpdateAsync(employee)).Succeeded);
            Assert.Equal(HttpStatusCode.Redirect, (await browser.GetAsync("/staff/dashboard")).StatusCode);
        }
        finally { await context.Client.DropDatabaseAsync(database); }
    }
}
