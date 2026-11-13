using System.Net;
using System.Text.RegularExpressions;
using HoleInHub.Business.Accounts;
using HoleInHub.Data.Accounts;
using HoleInHub.Data.Context;
using HoleInHub.Data.Migrations.Definitions;
using HoleInHub.Data.Reservations;
using HoleInHub.Web.Accounts;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using Xunit;

namespace HoleInHub.Tests.Web.Smoke;

public sealed partial class StaffPinAccessTests
{
    [Fact, Trait("Category", "Integration")]
    public async Task PinOnlySignInFindsTheEmployeeAndRejectsAmbiguousPins()
    {
        using var certificate = DesktopCertificate(DateTimeOffset.UtcNow);
        var database = "test_pin_only_" + Guid.NewGuid().ToString("N");
        await using var factory = Factory(certificate, database);
        using var browser = Client(factory, true);
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
            await new Migration_017_StaffRoleAdministration().UpAsync(context.Database, default);
            var first = new AccountUser { Email = "first@example.test", EmailConfirmed = true };
            var second = new AccountUser { Email = "second@example.test", EmailConfirmed = true };
            foreach (var employee in new[] { first, second })
            {
                Assert.True((await users.CreateAsync(employee, "StaffTestPassword123!")).Succeeded);
                employee.Roles = [StaffRoles.Staff];
                Assert.True((await users.UpdateAsync(employee)).Succeeded);
            }
            Assert.True(await pins.TrySaveAsync(first.Id, hasher.HashPassword(first, access.PinInput(first, "281739")), first.SecurityStamp, default));
            Assert.Single(await pins.ReadCandidatesAsync(default));
            var html = await browser.GetStringAsync("/staff/sign-in");
            Assert.DoesNotContain("name=\"Email\"", html);
            Assert.Contains("staff-auth-links", html);
            using var signedIn = await SubmitAsync(browser, "/staff/sign-in", new() { ["Pin"] = "281739" });
            Assert.Equal(HttpStatusCode.Redirect, signedIn.StatusCode);
            Assert.Equal("/staff", signedIn.Headers.Location?.OriginalString);
            Assert.True(await pins.TrySaveAsync(second.Id, hasher.HashPassword(second, access.PinInput(second, "281739")), second.SecurityStamp, default));
            using var ambiguous = await SubmitAsync(browser, "/staff/sign-in", new() { ["Pin"] = "281739" });
            Assert.Equal(HttpStatusCode.OK, ambiguous.StatusCode);
            Assert.DoesNotContain(ambiguous.Headers.TryGetValues("Set-Cookie", out var cookies) ? cookies : [], cookie => cookie.StartsWith("__Host-HihStaffPin=", StringComparison.Ordinal));
        }
        finally { await context.Client.DropDatabaseAsync(database); }
    }

    [Fact, Trait("Category", "Integration")]
    public async Task PinSetupRequiresBrowserBoundEmailCodeAndConsumesItWhenSaving()
    {
        using var certificate = DesktopCertificate(DateTimeOffset.UtcNow);
        var database = "test_pin_email_" + Guid.NewGuid().ToString("N");
        await using var factory = Factory(certificate, database);
        using var browser = Client(factory, true);
        using var otherBrowser = Client(factory, true);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IMongoDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AccountUser>>();
        var pins = scope.ServiceProvider.GetRequiredService<StaffPinService>();
        try
        {
            await new Migration_002_Reservations().UpAsync(context.Database, default);
            await new Migration_004_BookingProcess().UpAsync(context.Database, default);
            await new AccountsMigration().UpAsync(context.Database, default);
            await new Migration_016_StaffPinAttempts().UpAsync(context.Database, default);
            await new Migration_017_StaffRoleAdministration().UpAsync(context.Database, default);
            var employee = new AccountUser { Email = "staff@example.test", EmailConfirmed = true };
            Assert.True((await users.CreateAsync(employee, "StaffTestPassword123!")).Succeeded);
            employee.Roles = [StaffRoles.Staff];
            Assert.True((await users.UpdateAsync(employee)).Succeeded);
            using var requested = await SubmitAsync(browser, "/staff/pin/setup", new()
            { ["Email"] = employee.Email, ["Password"] = "StaffTestPassword123!", ["Pin"] = "281739", ["ConfirmPin"] = "281739" });
            Assert.Null(await pins.ReadAsync(employee.Id, default));
            Assert.Equal("/staff/pin/verify", requested.Headers.Location?.OriginalString);
            var code = await ReadPinCodeAsync(scope.ServiceProvider, employee.Email);
            using var unbound = await SubmitAsync(otherBrowser, "/staff/pin/setup", "/staff/pin/verify", new() { ["code"] = code });
            Assert.Equal("/staff/pin/setup", unbound.Headers.Location?.OriginalString);
            using var bypass = await SubmitAsync(browser, "/staff/pin/setup", "/staff/pin/new", new() { ["Pin"] = "281739", ["ConfirmPin"] = "281739" });
            Assert.Null(await pins.ReadAsync(employee.Id, default));
            using var wrong = await SubmitAsync(browser, "/staff/pin/verify", new() { ["code"] = "000000" });
            Assert.Equal(HttpStatusCode.OK, wrong.StatusCode);
            using var verified = await SubmitAsync(browser, "/staff/pin/verify", new() { ["code"] = code });
            Assert.Equal("/staff/pin/new", verified.Headers.Location?.OriginalString);
            var savedContext = requested.Headers.GetValues("Set-Cookie").Single(value => value.StartsWith("__Host-HihPinSetup=", StringComparison.Ordinal)).Split(';')[0];
            using var saved = await SubmitAsync(browser, "/staff/pin/new", new() { ["Pin"] = "281739", ["ConfirmPin"] = "281739" });
            Assert.Equal("/staff/sign-in", saved.Headers.Location?.OriginalString);
            var credential = await pins.ReadAsync(employee.Id, default);
            Assert.NotNull(credential);
            var proof = await context.Database.GetCollection<GuestVerificationRecord>("guest_verifications").Find(entry => entry.Email == employee.Email).SingleAsync();
            Assert.Equal("StaffPinSetup", proof.ConsumedBySubmissionKey);
            var beforeReplay = (await users.FindByIdAsync(employee.Id))!;
            Assert.False(await scope.ServiceProvider.GetRequiredService<StaffPinPersistence>().TrySaveAsync(
                new StaffPinCredential(employee.Id, "replayed-hash", Guid.NewGuid().ToString("N")), employee.SecurityStamp,
                StaffRoles.Workspace, default, new(proof.Id, employee.Email, DateTime.UtcNow), await pins.ReadEnrollmentVersionAsync(default)));
            Assert.Equal(beforeReplay.Version, (await users.FindByIdAsync(employee.Id))!.Version);
            browser.DefaultRequestHeaders.TryAddWithoutValidation("Cookie", savedContext);
            using var replay = await SubmitAsync(browser, "/staff/pin/setup", "/staff/pin/new", new() { ["Pin"] = "381749", ["ConfirmPin"] = "381749" });
            Assert.Equal(credential.Version, (await pins.ReadAsync(employee.Id, default))!.Version);
        }
        finally { await context.Client.DropDatabaseAsync(database); }
    }

    [Theory, Trait("Category", "Integration")]
    [InlineData("expired")]
    [InlineData("five-attempts")]
    [InlineData("changed-stamp")]
    [InlineData("revoked-role")]
    public async Task PinEmailVerificationFailsClosedWhenProofOrAccountChanges(string change)
    {
        using var certificate = DesktopCertificate(DateTimeOffset.UtcNow);
        var database = "test_pin_invalid_" + Guid.NewGuid().ToString("N");
        await using var factory = Factory(certificate, database);
        using var browser = Client(factory, true);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IMongoDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AccountUser>>();
        var pins = scope.ServiceProvider.GetRequiredService<StaffPinService>();
        try
        {
            await new Migration_002_Reservations().UpAsync(context.Database, default);
            await new Migration_004_BookingProcess().UpAsync(context.Database, default);
            await new AccountsMigration().UpAsync(context.Database, default);
            await new Migration_016_StaffPinAttempts().UpAsync(context.Database, default);
            await new Migration_017_StaffRoleAdministration().UpAsync(context.Database, default);
            var employee = new AccountUser { Email = "staff@example.test", EmailConfirmed = true };
            Assert.True((await users.CreateAsync(employee, "StaffTestPassword123!")).Succeeded);
            employee.Roles = [StaffRoles.Staff];
            Assert.True((await users.UpdateAsync(employee)).Succeeded);
            using var requested = await SubmitAsync(browser, "/staff/pin/setup", new() { ["Email"] = employee.Email, ["Password"] = "StaffTestPassword123!" });
            Assert.Equal("/staff/pin/verify", requested.Headers.Location?.OriginalString);
            var code = await ReadPinCodeAsync(scope.ServiceProvider, employee.Email);
            var proofs = context.Database.GetCollection<GuestVerificationRecord>("guest_verifications");
            if (change == "expired")
            {
                await proofs.UpdateOneAsync(entry => entry.Email == employee.Email, Builders<GuestVerificationRecord>.Update.Set(entry => entry.ExpiresUtc, DateTime.UtcNow.AddMinutes(-1)));
            }
            if (change == "five-attempts")
            {
                for (var i = 0; i < 5; i++)
                {
                    using var wrong = await SubmitAsync(browser, "/staff/pin/verify", new() { ["code"] = "000000" });
                    Assert.Equal(HttpStatusCode.OK, wrong.StatusCode);
                }
            }
            if (change is "changed-stamp" or "revoked-role")
            {
                using var verified = await SubmitAsync(browser, "/staff/pin/verify", new() { ["code"] = code });
                Assert.Equal("/staff/pin/new", verified.Headers.Location?.OriginalString);
                if (change == "changed-stamp") { Assert.True((await users.UpdateSecurityStampAsync(employee)).Succeeded); }
                else { employee.Roles = ["Customer"]; Assert.True((await users.UpdateAsync(employee)).Succeeded); }
            }
            else
            {
                using var rejected = await SubmitAsync(browser, "/staff/pin/verify", new() { ["code"] = code });
                Assert.Equal(HttpStatusCode.OK, rejected.StatusCode);
            }
            using var save = await SubmitAsync(browser, "/staff/pin/setup", "/staff/pin/new", new() { ["Pin"] = "281739", ["ConfirmPin"] = "281739" });
            Assert.Equal("/staff/pin/setup", save.Headers.Location?.OriginalString);
            Assert.Null(await pins.ReadAsync(employee.Id, default));
        }
        finally { await context.Client.DropDatabaseAsync(database); }
    }

    private static async Task<string> ReadPinCodeAsync(IServiceProvider services, string email)
    {
        var context = services.GetRequiredService<IMongoDbContext>();
        var message = await context.Database.GetCollection<OutboxRecord>("outbox")
            .Find(entry => entry.Recipient == email && entry.Subject == "Verify your staff PIN change")
            .SortByDescending(entry => entry.DueUtc).FirstAsync();
        var text = services.GetRequiredService<IDataProtectionProvider>().CreateProtector("BookingEmail").Unprotect(message.Body["protected:".Length..]);
        return Regex.Match(text, @"\b[0-9]{6}\b").Value;
    }

    [Fact, Trait("Category", "Integration")]
    public async Task ConcurrentPinEnrollmentsRequireAnotherUniquenessCheckAfterOneCommits()
    {
        using var certificate = DesktopCertificate(DateTimeOffset.UtcNow);
        var database = "test_pin_race_" + Guid.NewGuid().ToString("N");
        await using var factory = Factory(certificate, database);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IMongoDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AccountUser>>();
        var persistence = scope.ServiceProvider.GetRequiredService<StaffPinPersistence>();
        try
        {
            await new AccountsMigration().UpAsync(context.Database, default);
            await new Migration_017_StaffRoleAdministration().UpAsync(context.Database, default);
            var version = (await persistence.ReadEnrollmentVersionAsync(default))!.Value;
            var staff = new[] { new AccountUser { Email = "first@example.test", EmailConfirmed = true }, new AccountUser { Email = "second@example.test", EmailConfirmed = true } };
            foreach (var employee in staff)
            {
                Assert.True((await users.CreateAsync(employee, "StaffTestPassword123!")).Succeeded);
                employee.Roles = [StaffRoles.Staff];
                Assert.True((await users.UpdateAsync(employee)).Succeeded);
                await context.Database.GetCollection<GuestVerificationRecord>("guest_verifications").InsertOneAsync(new GuestVerificationRecord
                { Id = employee.Id, Email = employee.Email, Purpose = "AccountEmail", VerifiedUtc = DateTime.UtcNow, ExpiresUtc = DateTime.UtcNow.AddMinutes(10) });
            }
            var results = await Task.WhenAll(staff.Select(employee => persistence.TrySaveAsync(
                new StaffPinCredential(employee.Id, "same-pin-hash", "version"), employee.SecurityStamp, StaffRoles.Workspace, default,
                new(employee.Id, employee.Email, DateTime.UtcNow), version)));
            Assert.Single(results, result => result);
            Assert.Equal(1, await context.Database.GetCollection<StaffPinCredential>("staff_pin_credentials").CountDocumentsAsync(FilterDefinition<StaffPinCredential>.Empty));
            Assert.Equal(version + 1, await persistence.ReadEnrollmentVersionAsync(default));
            var loser = staff[Array.IndexOf(results, false)];
            var unused = await context.Database.GetCollection<GuestVerificationRecord>("guest_verifications").Find(entry => entry.Id == loser.Id).SingleAsync();
            Assert.Null(unused.ConsumedBySubmissionKey);
            Assert.Null(await persistence.ReadAsync(loser.Id, default));
        }
        finally { await context.Client.DropDatabaseAsync(database); }
    }
}
