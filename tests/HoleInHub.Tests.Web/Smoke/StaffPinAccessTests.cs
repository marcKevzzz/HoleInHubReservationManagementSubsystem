using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using HoleInHub.Business.Accounts;
using HoleInHub.Data.Context;
using HoleInHub.Data.Migrations.Definitions;
using HoleInHub.Data.Reservations;
using HoleInHub.Web.Accounts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Xunit;

namespace HoleInHub.Tests.Web.Smoke;

public sealed partial class StaffPinAccessTests
{
    internal sealed class DesktopTlsFixture(X509Certificate2 certificate) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, continuation) =>
            {
                if (context.Request.Headers["X-Test-Desktop"] == "approved")
                {
                    context.Connection.ClientCertificate = certificate;
                }
                if (IPAddress.TryParse(context.Request.Headers["X-Test-Address"], out var address))
                {
                    context.Connection.RemoteIpAddress = address;
                }
                await continuation();
            });
            next(app);
        };
    }

    internal static X509Certificate2 DesktopCertificate(DateTimeOffset now)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=Test Desktop", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return request.CreateSelfSigned(now.AddMinutes(-5), now.AddDays(1));
    }

    private static WebApplicationFactory<Program> Factory(X509Certificate2 certificate, string database, bool enabled = true, bool presentation = false) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder
            .UseEnvironment(presentation ? "Development" : "Testing").ConfigureLogging(logging => logging.ClearProviders())
            .ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Development:StaffWorkspaceUrl"] = "",
                ["StaffPin:Enabled"] = enabled.ToString(),
                ["StaffPin:Host"] = "staff.example.test",
                ["StaffPin:Pepper"] = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),
                ["StaffPin:DesktopFingerprints:0"] = presentation ? null : certificate.GetCertHashString(HashAlgorithmName.SHA256),
                ["StaffPin:PresentationAccount"] = presentation ? "staff@example.test" : null,
                ["MongoDb:ConnectionString"] = "mongodb://localhost:27017/?replicaSet=rs0",
                ["MongoDb:DatabaseName"] = database,
                ["MongoDb:ReplicaSetName"] = "rs0",
                ["Smtp:Host"] = ""
            }))
            .ConfigureTestServices(services =>
            {
                services.AddDataProtection().UseEphemeralDataProtectionProvider();
                services.AddSingleton<IStartupFilter>(new DesktopTlsFixture(certificate));
            }));

    private static HttpClient Client(WebApplicationFactory<Program> factory, bool desktop)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://staff.example.test"),
            AllowAutoRedirect = false
        });
        if (desktop) { client.DefaultRequestHeaders.Add("X-Test-Desktop", "approved"); }
        return client;
    }

    [Fact]
    public async Task LocalStaffEntryRedirectsToConfiguredHttpsWorkspaceWithoutAffectingOtherHostsOrLooping()
    {
        using var certificate = DesktopCertificate(DateTimeOffset.UtcNow);
        using var factory = Factory(certificate, "test_staff_entry_redirect", enabled: false)
            .WithWebHostBuilder(builder => builder.UseEnvironment("Development")
                .ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
                { ["Development:StaffWorkspaceUrl"] = "https://localhost:7089" })));
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        foreach (var source in new[] { "http://localhost:5275/staff/sign-in", "https://localhost:7077/staff/sign-in" })
        {
            using var response = await client.GetAsync(source);
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Equal("https://localhost:7089/staff/sign-in", response.Headers.Location?.AbsoluteUri);
        }
        using var schedule = await client.GetAsync("http://localhost:5275/staff?date=2026-11-09");
        Assert.Equal("https://localhost:7089/staff?date=2026-11-09", schedule.Headers.Location?.AbsoluteUri);
        using var destination = await client.GetAsync("https://localhost:7089/staff/sign-in");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, destination.StatusCode);
        Assert.Null(destination.Headers.Location);
        using var otherHost = await client.GetAsync("http://public.example.test/staff/sign-in");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, otherHost.StatusCode);
        using var home = await client.GetAsync("http://localhost:5275/");
        Assert.Equal(HttpStatusCode.OK, home.StatusCode);

        var accountScheme = IdentityConstants.ApplicationScheme;
        var cookie = factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(accountScheme);
        var account = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "test-admin"), new Claim(ClaimTypes.Role, StaffRoles.Admin), new Claim("amr", "pwd")],
            accountScheme));
        var now = DateTimeOffset.UtcNow;
        var ticket = new AuthenticationTicket(account,
            new AuthenticationProperties { IssuedUtc = now, ExpiresUtc = now.AddMinutes(10) }, accountScheme);
        client.DefaultRequestHeaders.TryAddWithoutValidation("Cookie", $"{cookie.Cookie.Name}={cookie.TicketDataFormat.Protect(ticket)}");
        using var dashboard = await client.GetAsync("https://localhost:7077/staff/dashboard");
        Assert.Equal(HttpStatusCode.Redirect, dashboard.StatusCode);
        Assert.Equal(7077, dashboard.Headers.Location?.Port);
        Assert.Equal("/account/sign-in", dashboard.Headers.Location?.AbsolutePath);
    }

    [Theory]
    [Trait("Category", "Integration")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DemoStaffAndManagerUsePinsAndAdminUsesPassword(bool isolatedDemo)
    {
        using var certificate = DesktopCertificate(DateTimeOffset.UtcNow);
        var database = (isolatedDemo ? "hih_role_demo_test_" : "test_staff_roles_") + Guid.NewGuid().ToString("N");
        await using var factory = Factory(certificate, database, presentation: true).WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["StaffPin:Host"] = "localhost",
                ["StaffPin:PresentationManagerAccount"] = "manager@example.test",
                ["MongoDb:ConnectionString"] = "mongodb://localhost:27017/?replicaSet=rs0&directConnection=true"
            })).ConfigureTestServices(services => services.AddSingleton<IStartupFilter, LoopbackFixture>()));
        using var browser = factory.CreateClient(new WebApplicationFactoryClientOptions
        { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
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
            var employees = new List<AccountUser>();
            var roles = new[] { "Staff", "Manager", "Admin" };
            var codes = new[] { "048217", "582049", "738294" };
            for (var i = 0; i < roles.Length; i++)
            {
                var employee = new AccountUser { Email = roles[i].ToLowerInvariant() + "@example.test", EmailConfirmed = true };
                Assert.True((await users.CreateAsync(employee, "StaffTestPassword123!")).Succeeded);
                employee.Roles = [roles[i]];
                Assert.True((await users.UpdateAsync(employee)).Succeeded);
                Assert.True(await pins.TrySaveAsync(employee.Id, hasher.HashPassword(employee, access.PinInput(employee, codes[i])),
                    employee.SecurityStamp, default));
                employees.Add(employee);
            }
            var html = await browser.GetStringAsync("/staff/sign-in");
            Assert.DoesNotContain("Choose a demo workspace", html);
            Assert.DoesNotContain("workspace=", html);
            for (var i = 0; i < roles.Length - 1; i++)
            {
                using var signedIn = await SubmitAsync(browser, "/staff/sign-in", new() { ["Pin"] = codes[i] });
                Assert.Equal(HttpStatusCode.Redirect, signedIn.StatusCode);
                Assert.Equal(new[] { "/staff", "/staff/dashboard" }[i], signedIn.Headers.Location?.OriginalString);
                var cookieName = isolatedDemo ? "__Host-HihRoleDemoPin=" : "__Host-HihStaffPin=";
                Assert.Contains(signedIn.Headers.GetValues("Set-Cookie"), value => value.StartsWith(cookieName, StringComparison.Ordinal));
            }
            using var admin = await SubmitAsync(browser, "/account/sign-in",
                new() { ["Email"] = employees[2].Email, ["Password"] = "StaffTestPassword123!" });
            Assert.Equal(HttpStatusCode.Redirect, admin.StatusCode);
            Assert.Equal("/staff/dashboard", admin.Headers.Location?.OriginalString);
            var rolePath = $"/admin/users/{employees[0].Id}/role";
            Assert.DoesNotContain(rolePath, await browser.GetStringAsync("/staff/people"));
            Assert.Equal(HttpStatusCode.NotFound, (await browser.GetAsync(rolePath)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound,
                (await SubmitAsync(browser, "/staff/people", rolePath, new() { ["Form.Role"] = "Admin" })).StatusCode);
            Assert.True(await pins.TrySaveAsync(employees[1].Id, hasher.HashPassword(employees[1], access.PinInput(employees[1], codes[0])),
                employees[1].SecurityStamp, default));
            using var ambiguous = await SubmitAsync(browser, "/staff/sign-in", new() { ["Pin"] = codes[0] });
            Assert.Equal(HttpStatusCode.OK, ambiguous.StatusCode);
            Assert.Contains("That PIN didn", await ambiguous.Content.ReadAsStringAsync());
        }
        finally { await context.Client.DropDatabaseAsync(database); }
    }

    private sealed class LoopbackFixture : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, continuation) => { context.Connection.RemoteIpAddress = IPAddress.Loopback; return continuation(); });
            next(app);
        };
    }

    [Fact]
    public async Task PresentationFormUsesOnlyThePinWithoutAnEnrolledCertificate()
    {
        using var certificate = DesktopCertificate(DateTimeOffset.UtcNow);
        using var factory = Factory(certificate, "test_pin_presentation", presentation: true);
        using var browser = Client(factory, false);
        var html = await browser.GetStringAsync("/staff/sign-in");
        Assert.Contains("data-pin-entry", html);
        Assert.Contains("name=\"Pin\"", html);
        Assert.DoesNotContain("name=\"Email\"", html);
        Assert.Equal(HttpStatusCode.Forbidden, (await browser.GetAsync("https://public.example.test/staff/sign-in")).StatusCode);
    }

    [Fact]
    public async Task AdminWorkspaceChallengesAtItsOwnSignInPath()
    {
        using var certificate = DesktopCertificate(DateTimeOffset.UtcNow);
        using var factory = Factory(certificate, "test_admin_sign_in_route");
        using var desktop = Client(factory, true);

        using var admin = await desktop.GetAsync("/admin/users");
        Assert.Equal(HttpStatusCode.Redirect, admin.StatusCode);
        Assert.Equal("/account/sign-in", admin.Headers.Location?.AbsolutePath);

        using var manager = await desktop.GetAsync("/manager");
        Assert.Equal(HttpStatusCode.Redirect, manager.StatusCode);
        Assert.Equal("/staff/sign-in", manager.Headers.Location?.AbsolutePath);
    }

    [Fact]
    public async Task PinFormAndSetupRequireEnrolledTlsDesktop()
    {
        using var certificate = DesktopCertificate(DateTimeOffset.UtcNow);
        using var factory = Factory(certificate, "test_pin_desktop_gate");
        using var phone = Client(factory, false);
        using var desktop = Client(factory, true);
        phone.DefaultRequestHeaders.Add("X-Client-Cert", Convert.ToBase64String(certificate.RawData));
        Assert.Equal(HttpStatusCode.Forbidden, (await phone.GetAsync("/staff/sign-in")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await phone.GetAsync("/staff/pin/setup")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await desktop.GetAsync("/staff/sign-in")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await desktop.GetAsync("/staff/pin/setup")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await desktop.PostAsync("/staff/sign-in", new FormUrlEncodedContent([]))).StatusCode);
        using var publicSignIn = await desktop.GetAsync("https://public.example.test/staff/sign-in");
        Assert.Equal(HttpStatusCode.Forbidden, publicSignIn.StatusCode);
        Assert.Contains("href=\"/account/sign-in\"", await publicSignIn.Content.ReadAsStringAsync());
        using var publicAttempt = await SubmitAsync(desktop, "https://public.example.test/account/sign-in",
            "https://public.example.test/staff/sign-in", []);
        Assert.Equal(HttpStatusCode.Forbidden, publicAttempt.StatusCode);
    }

    [Theory]
    [InlineData(false, "Staff")]
    [InlineData(true, "Manager")]
    [InlineData(true, "Admin")]
    public async Task CustomerCookieCannotOpenStaffRoutes(bool enabled, string role)
    {
        using var certificate = DesktopCertificate(DateTimeOffset.UtcNow);
        using var factory = Factory(certificate, "test_staff_sign_out", enabled);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://public.example.test"),
            AllowAutoRedirect = false
        });
        var scheme = IdentityConstants.ApplicationScheme;
        var cookie = factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(scheme);
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "test-employee"), new Claim(ClaimTypes.Role, role),
                new Claim(ClaimTypes.AuthenticationMethod, "Google")], scheme));
        var now = DateTimeOffset.UtcNow;
        var ticket = new AuthenticationTicket(principal,
            new AuthenticationProperties { IssuedUtc = now, ExpiresUtc = now.AddMinutes(10) }, scheme);
        client.DefaultRequestHeaders.TryAddWithoutValidation("Cookie", $"{cookie.Cookie.Name}={cookie.TicketDataFormat.Protect(ticket)}");

        using var workspace = await client.GetAsync("/staff");
        Assert.Equal(HttpStatusCode.Redirect, workspace.StatusCode);
        Assert.Equal("/staff/sign-in", workspace.Headers.Location?.AbsolutePath);
        using var adminPage = await client.GetAsync("/staff/reservations/blocks/new");
        Assert.Equal(HttpStatusCode.Redirect, adminPage.StatusCode);
        Assert.Equal("/staff/sign-in", adminPage.Headers.Location?.AbsolutePath);
        var roleEdit = "/admin/users/test-target/role";
        using var rejected = await client.GetAsync(roleEdit);
        Assert.Equal(HttpStatusCode.Redirect, rejected.StatusCode);
        Assert.Equal("/account/sign-in", rejected.Headers.Location?.AbsolutePath);
        Assert.Equal(HttpStatusCode.Redirect, (await client.PostAsync(roleEdit, new StringContent(""))).StatusCode);
        using var signIn = await client.GetAsync("/staff/sign-in");
        Assert.Equal(enabled ? HttpStatusCode.Forbidden : HttpStatusCode.ServiceUnavailable, signIn.StatusCode);
    }

    [Theory, Trait("Category", "Integration")]
    [InlineData("Staff", false)]
    [InlineData("Manager", false)]
    [InlineData("Staff", true)]
    public async Task PinSessionRevokesAfterSwitchOrRoleRemoval(string role, bool presentation)
    {
        using var certificate = DesktopCertificate(DateTimeOffset.UtcNow);
        var database = "test_staff_pin_" + Guid.NewGuid().ToString("N");
        await using var factory = Factory(certificate, database, presentation: presentation);
        using var desktop = Client(factory, !presentation);
        using var phone = Client(factory, false);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IMongoDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AccountUser>>();
        try
        {
            await new Migration_002_Reservations().UpAsync(context.Database, default);
            await new AccountsMigration().UpAsync(context.Database, default);
            await new Migration_016_StaffPinAttempts().UpAsync(context.Database, default);
            var employee = new AccountUser { Email = "staff@example.test", EmailConfirmed = true };
            const string password = "StaffTestPassword123!";
            const string pin = "048217";
            Assert.True((await users.CreateAsync(employee, password)).Succeeded);
            employee.Roles = [role];
            Assert.True((await users.UpdateAsync(employee)).Succeeded);
            var credentials = new Dictionary<string, string> { ["Pin"] = pin };
            var access = scope.ServiceProvider.GetRequiredService<StaffPinAccess>();
            var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<AccountUser>>();
            var pins = scope.ServiceProvider.GetRequiredService<StaffPinService>();
            Assert.True(await pins.TrySaveAsync(employee.Id, hasher.HashPassword(employee, access.PinInput(employee, pin)),
                employee.SecurityStamp, default));
            using var wrong = await SubmitAsync(desktop, "/staff/sign-in", new() { ["Pin"] = "048216" });
            Assert.Contains("That PIN didn", await wrong.Content.ReadAsStringAsync());
            var first = await SubmitAsync(desktop, "/staff/sign-in", credentials);
            Assert.Equal(HttpStatusCode.Redirect, first.StatusCode);
            Assert.Equal(role == "Manager" ? "/staff/dashboard" : "/staff", first.Headers.Location?.OriginalString);
            var cookie = Assert.Single(first.Headers.GetValues("Set-Cookie"), value => value.StartsWith("__Host-HihStaffPin=", StringComparison.Ordinal));
            if (!presentation) { phone.DefaultRequestHeaders.TryAddWithoutValidation("Cookie", cookie.Split(';')[0]); }
            Assert.Equal(HttpStatusCode.Redirect, (await phone.GetAsync("/staff")).StatusCode);
            using var workspace = await desktop.GetAsync("/staff");
            Assert.Equal(HttpStatusCode.OK, workspace.StatusCode);
            var workspaceHtml = await workspace.Content.ReadAsStringAsync();
            Assert.Contains("id=\"account-logout-form\"", workspaceHtml);
            Assert.Contains("id=\"logout-confirmation\"", workspaceHtml);
            Assert.Contains("form=\"account-logout-form\"", workspaceHtml);
            Assert.Contains("/js/account-menu.js", workspaceHtml);
            Assert.Contains("/js/staff-timeline-gestures.js", workspaceHtml);
            var drawerBooking = new ReservationRecord
            {
                AccountId = "guest:drawer",
                Reference = "HIH-DRAWER",
                Service = "Regular",
                Status = "Confirmed",
                ResourceIds = ["bay-2"],
                ResourceNames = new() { ["bay-2"] = "Bay 1" },
                FirstName = "Preview",
                LastName = "Guest",
                Phone = "09175550123",
                Attendees = 1,
                StartUtc = DateTime.UtcNow.AddDays(2),
                EndUtc = DateTime.UtcNow.AddDays(2).AddHours(1),
                IdempotencyKey = Guid.NewGuid().ToString("N"),
                SubmissionHash = "drawer-preview"
            };
            await context.Database.GetCollection<ReservationRecord>("reservations").InsertOneAsync(drawerBooking);
            using var drawerActions = await desktop.GetAsync($"/staff/reservations/{drawerBooking.Id}/actions?drawer=true");
            Assert.Equal(HttpStatusCode.OK, drawerActions.StatusCode);
            var drawerHtml = await drawerActions.Content.ReadAsStringAsync();
            Assert.Contains("Check-in opens at", drawerHtml);
            Assert.DoesNotContain("<html", drawerHtml);
            var searchHtml = await desktop.GetStringAsync("/staff/reservations/search?reference=HIH-DRAWER");
            Assert.Contains("Bay 1", searchHtml);
            Assert.Contains("staff-search-slot", searchHtml);
            Assert.Contains("data-status=\"Confirmed\"", searchHtml);
            Assert.Equal(HttpStatusCode.Redirect,
                (await phone.GetAsync($"/staff/reservations/{drawerBooking.Id}/actions?drawer=true")).StatusCode);
            Assert.DoesNotContain("title=\"Change requests\"", workspaceHtml);
            var expiredPage = await desktop.GetStringAsync("/staff/review-queue/expired");
            Assert.Contains("No expired requests", expiredPage);
            Assert.DoesNotContain("Times are in Manila.", expiredPage);
            Assert.DoesNotContain("aria-label=\"Request type\"", expiredPage);
            using var legacyQueue = await desktop.GetAsync("/staff/review-queue?filter=expired&day=2026-11-10&sort=session");
            Assert.Equal(HttpStatusCode.Redirect, legacyQueue.StatusCode);
            Assert.Equal("/staff/review-queue/expired?day=2026-11-10&sort=session", legacyQueue.Headers.Location?.OriginalString);
            using var oldClosedQueue = await desktop.GetAsync("/staff/review-queue/closed");
            Assert.Equal(HttpStatusCode.Redirect, oldClosedQueue.StatusCode);
            Assert.Equal("/staff/review-queue/expired", oldClosedQueue.Headers.Location?.OriginalString);
            using var customer = await desktop.GetAsync("/my-reservations");
            Assert.Equal(HttpStatusCode.Redirect, customer.StatusCode);
            Assert.Equal("/account/sign-in", customer.Headers.Location?.AbsolutePath);
            if (role == "Staff")
            {
                Assert.Equal(HttpStatusCode.Forbidden, (await desktop.GetAsync("/staff/reservations/blocks/new")).StatusCode);
            }
            var dashboard = await desktop.GetStringAsync("/staff/dashboard");
            Assert.Contains("pending review", dashboard);
            Assert.Contains("aria-current=\"page\"", dashboard);
            Assert.Contains("aria-label=\"Reservations\"", dashboard);
            Assert.Equal(role != "Staff", dashboard.Contains("Team &amp; access"));
            Assert.Equal(role == "Staff" ? HttpStatusCode.Forbidden : HttpStatusCode.Redirect,
                (await desktop.GetAsync("/manager/availability?date=2026-11-10")).StatusCode);
            if (role == "Manager")
            {
                using var availability = await desktop.GetAsync("/manager/availability?date=2026-11-10");
                Assert.Equal("/staff?date=2026-11-10", availability.Headers.Location?.OriginalString);
                Assert.DoesNotContain("Review days", await desktop.GetStringAsync("/staff/settings"));
            }
            else
            {
                Assert.Equal(HttpStatusCode.Forbidden, (await desktop.GetAsync("/staff/people")).StatusCode);
                Assert.Equal(HttpStatusCode.Forbidden, (await desktop.GetAsync("/staff/settings")).StatusCode);
            }
            Assert.Equal(role == "Admin" ? HttpStatusCode.OK : HttpStatusCode.Forbidden,
                (await desktop.GetAsync("/admin/users")).StatusCode);
            var switchToken = Token(await desktop.GetStringAsync("/staff"));
            using var switched = await desktop.PostAsync("/staff/switch",
                new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = switchToken }));
            Assert.Equal(HttpStatusCode.Redirect, switched.StatusCode);
            Assert.Equal("/staff/sign-in", switched.Headers.Location?.OriginalString);
            Assert.Equal(HttpStatusCode.Redirect, (await desktop.GetAsync("/staff")).StatusCode);
            Assert.Equal(HttpStatusCode.Redirect, (await SubmitAsync(desktop, "/staff/sign-in", credentials)).StatusCode);
            employee = (await users.FindByIdAsync(employee.Id))!;
            employee.Roles = [role == "Staff" ? "Customer" : "Staff"];
            Assert.True((await users.UpdateAsync(employee)).Succeeded);
            Assert.Equal(HttpStatusCode.Redirect, (await desktop.GetAsync("/staff")).StatusCode);
            var attempts = scope.ServiceProvider.GetRequiredService<HoleInHub.Data.Accounts.StaffPinPersistence>();
            var now = DateTime.UtcNow;
            var parallel = await Task.WhenAll(Enumerable.Range(0, 12)
                .Select(_ => attempts.TakeAttemptAsync("concurrent", now, 5, default)));
            Assert.Equal(5, parallel.Count(granted => granted));
        }
        finally { await context.Client.DropDatabaseAsync(database); }
    }


    [Fact, Trait("Category", "Integration")]
    public async Task AdminChangesStaffAccessAndRevokesOldDesktopSession()
    {
        using var certificate = DesktopCertificate(DateTimeOffset.UtcNow);
        var database = "test_staff_roles_" + Guid.NewGuid().ToString("N");
        await using var factory = Factory(certificate, database).WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.Configure<SecurityStampValidatorOptions>(options => options.ValidationInterval = TimeSpan.Zero)));
        using var admin = Client(factory, false);
        admin.BaseAddress = new Uri("https://public.example.test");
        using var employee = Client(factory, true);
        using var anonymous = Client(factory, true);
        admin.DefaultRequestHeaders.Add("X-Test-Address", "192.0.2.1");
        employee.DefaultRequestHeaders.Add("X-Test-Address", "192.0.2.2");
        anonymous.DefaultRequestHeaders.Add("X-Test-Address", "192.0.2.3");
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IMongoDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AccountUser>>();
        const string password = "StaffTestPassword123!";
        try
        {
            await new AccountsMigration().UpAsync(context.Database, default);
            await new Migration_016_StaffPinAttempts().UpAsync(context.Database, default);
            var adminAccount = await CreateAsync("admin@example.test", StaffRoles.Admin);
            var staffAccount = await CreateAsync("employee@example.test", StaffRoles.Staff);
            var customer = await CreateAsync("customer@example.test", "Customer");
            var pinAccess = scope.ServiceProvider.GetRequiredService<StaffPinAccess>();
            var pins = scope.ServiceProvider.GetRequiredService<StaffPinService>();
            var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<AccountUser>>();
            const string oldAdminPin = "048217";
            Assert.True(await pins.TrySaveAsync(adminAccount.Id,
                hasher.HashPassword(adminAccount, pinAccess.PinInput(adminAccount, oldAdminPin)), adminAccount.SecurityStamp, default));
            using var rejectedPin = await SubmitAsync(anonymous, "/staff/sign-in",
                new() { ["Email"] = adminAccount.Email, ["Pin"] = oldAdminPin });
            Assert.Equal(HttpStatusCode.OK, rejectedPin.StatusCode);
            Assert.Equal(HttpStatusCode.Redirect, (await anonymous.GetAsync("/admin")).StatusCode);
            var path = $"/admin/users/{staffAccount.Id}/role";
            Assert.Equal(HttpStatusCode.Redirect, (await anonymous.GetAsync(path)).StatusCode);
            Assert.Equal(HttpStatusCode.Redirect, (await anonymous.GetAsync("/admin/activity")).StatusCode);
            using var adminLogin = await SubmitAsync(admin, "/account/sign-in",
                new() { ["Email"] = adminAccount.Email, ["Password"] = password });
            Assert.Equal(HttpStatusCode.Redirect, adminLogin.StatusCode);
            Assert.Equal("/staff/dashboard", adminLogin.Headers.Location?.OriginalString);
            Assert.Contains("/staff/activity", await admin.GetStringAsync("/staff/dashboard"));
            using var legacyActivity = await admin.GetAsync("/admin/activity");
            Assert.Equal(HttpStatusCode.Redirect, legacyActivity.StatusCode);
            Assert.Equal("/staff/activity?category=access", legacyActivity.Headers.Location?.OriginalString);
            using var legacyAdmin = await admin.GetAsync("/admin");
            Assert.Equal("/staff/dashboard", legacyAdmin.Headers.Location?.OriginalString);
            using var legacyUsers = await admin.GetAsync("/admin/users?page=2");
            Assert.Equal("/staff/people?page=2", legacyUsers.Headers.Location?.OriginalString);
            Assert.Contains("Review days", await admin.GetStringAsync("/staff/settings"));
            Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/staff")).StatusCode);
            await SignInAsync(employee, staffAccount.Email, "281739");
            staffAccount = (await users.FindByIdAsync(staffAccount.Id))!;
            Assert.Equal(HttpStatusCode.Forbidden, (await employee.GetAsync(path)).StatusCode);
            var directory = await admin.GetStringAsync("/staff/people");
            Assert.Contains($"/staff/people/{staffAccount.Id}", directory);
            Assert.Contains(path, await admin.GetStringAsync($"/staff/people/{staffAccount.Id}"));
            Assert.DoesNotContain(customer.Email, directory);
            Assert.Contains(adminAccount.Email, directory);
            Assert.Contains(adminAccount.Email, await admin.GetStringAsync("/staff/people?scope=Admin"));
            Assert.DoesNotContain(staffAccount.Email, await admin.GetStringAsync("/staff/people?scope=Admin"));
            Assert.DoesNotContain(customer.Email, await admin.GetStringAsync("/staff/people?scope=team"));
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync(path, new StringContent(""))).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await SubmitAsync(admin, path, Fields("Owner", staffAccount.Version))).StatusCode);

            Assert.Equal(HttpStatusCode.ServiceUnavailable,
                (await SubmitAsync(admin, path, Fields(StaffRoles.Manager, staffAccount.Version))).StatusCode);
            await new Migration_017_StaffRoleAdministration().UpAsync(context.Database, default);
            Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/staff/activity?category=access")).StatusCode);
            admin.DefaultRequestHeaders.Add("X-Correlation-ID", "untrusted-correlation");
            using var roleChanged = await SubmitAsync(admin, path, Fields(StaffRoles.Manager, staffAccount.Version));
            Assert.Equal(HttpStatusCode.Redirect, roleChanged.StatusCode);
            var changeFeedback = await admin.GetStringAsync(roleChanged.Headers.Location);
            Assert.Contains("data-toast-seed data-kind=\"success\"", changeFeedback);
            Assert.Contains("Staff access updated", changeFeedback);
            Assert.Contains("New role: Manager.", changeFeedback);
            Assert.DoesNotContain("data-toast-seed", await admin.GetStringAsync(roleChanged.Headers.Location));
            Assert.Equal(HttpStatusCode.Conflict, (await SubmitAsync(admin, path, Fields(StaffRoles.Manager, staffAccount.Version))).StatusCode);
            Assert.Equal(HttpStatusCode.Redirect, (await employee.GetAsync("/manager/team")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await SubmitAsync(employee, "/staff/sign-in",
                new() { ["Email"] = staffAccount.Email, ["Pin"] = "281739" })).StatusCode);
            await SignInAsync(employee, staffAccount.Email, "281740");
            using var legacyTeam = await employee.GetAsync("/manager/team?page=2");
            Assert.Equal("/staff/people?page=2&scope=team", legacyTeam.Headers.Location?.OriginalString);
            var team = await employee.GetStringAsync("/staff/people?scope=Manager");
            Assert.Contains(staffAccount.Email, team);
            Assert.DoesNotContain(customer.Email, team);
            var person = await employee.GetStringAsync($"/staff/people/{staffAccount.Id}");
            Assert.Contains(staffAccount.Email, person);
            Assert.DoesNotContain("Edit staff access", person);
            Assert.Equal(HttpStatusCode.NotFound, (await employee.GetAsync($"/staff/people/{customer.Id}")).StatusCode);
            var ownProfile = await admin.GetStringAsync($"/staff/people/{adminAccount.Id}");
            Assert.Contains("Your account", ownProfile);
            Assert.DoesNotContain("Edit staff access", ownProfile);
            Assert.DoesNotContain("Edit staff access", team);
            Assert.Equal(HttpStatusCode.Forbidden, (await employee.GetAsync("/admin/activity")).StatusCode);
            var activity = await admin.GetStringAsync("/staff/activity?category=access");
            var eventLink = Regex.Match(activity, "href=\"(/staff/activity/[^\"]+)\"");
            Assert.True(eventLink.Success, "The Access feed should link to the recorded role change.");
            var activityDetail = await admin.GetStringAsync(WebUtility.HtmlDecode(eventLink.Groups[1].Value));
            Assert.Contains("Roster change", activityDetail);
            Assert.Contains("Staff", activityDetail);
            Assert.Contains("Manager", activityDetail);
            Assert.DoesNotContain("untrusted-correlation", activity);
            var otherRolePath = $"/admin/users/{adminAccount.Id}/role";
            Assert.Equal(HttpStatusCode.Forbidden, (await employee.GetAsync(otherRolePath)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await employee.PostAsync(otherRolePath, new StringContent(""))).StatusCode);

            var updated = (await users.FindByIdAsync(staffAccount.Id))!;
            using var roleRemoved = await SubmitAsync(admin, path, Fields("None", updated.Version));
            Assert.Equal(HttpStatusCode.Redirect, roleRemoved.StatusCode);
            var removalFeedback = await admin.GetStringAsync(roleRemoved.Headers.Location);
            Assert.Contains("data-toast-seed data-kind=\"success\"", removalFeedback);
            Assert.Contains("Staff access removed", removalFeedback);
            Assert.Equal(HttpStatusCode.Redirect, (await employee.GetAsync("/manager/team")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await SubmitAsync(anonymous, "/staff/pin/setup",
                new() { ["Email"] = staffAccount.Email, ["Password"] = password, ["Pin"] = "281741", ["ConfirmPin"] = "281741" })).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await SubmitAsync(anonymous, "/staff/sign-in",
                new() { ["Email"] = staffAccount.Email, ["Pin"] = "281740" })).StatusCode);
            var revoked = (await users.FindByIdAsync(staffAccount.Id))!;
            Assert.Equal(new[] { "Customer" }, revoked.Roles);
            Assert.Equal(HttpStatusCode.Redirect,
                (await SubmitAsync(admin, path, Fields(StaffRoles.Staff, revoked.Version))).StatusCode);
            var restored = (await users.FindByIdAsync(staffAccount.Id))!;
            Assert.Equal(new[] { "Customer", StaffRoles.Staff }, restored.Roles);
            var audit = context.Database.GetCollection<HoleInHub.Data.Accounts.StaffRoleAuditRecord>("staff_role_audit");
            var events = await audit.Find(x => x.TargetAccountId == staffAccount.Id).ToListAsync();
            Assert.Equal(3, events.Count);
            Assert.All(events, entry => Assert.True(Guid.TryParseExact(entry.CorrelationId, "N", out _)));

            var currentAdmin = (await users.FindByIdAsync(adminAccount.Id))!;
            Assert.True((await users.UpdateSecurityStampAsync(currentAdmin)).Succeeded);
            using var expired = await admin.GetAsync("/admin/users");
            Assert.Equal(HttpStatusCode.Redirect, expired.StatusCode);
            Assert.Equal("/account/sign-in", expired.Headers.Location?.AbsolutePath);

            using var returnToBookings = await SubmitAsync(admin, "/account/sign-in", new()
            {
                ["Email"] = adminAccount.Email,
                ["Password"] = password,
                ["ReturnUrl"] = "/my-reservations"
            });
            Assert.Equal("/my-reservations", returnToBookings.Headers.Location?.OriginalString);
            Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/my-reservations")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/staff/people")).StatusCode);

            using var switchedAccount = await SubmitAsync(admin, "/account/sign-in",
                new() { ["Email"] = customer.Email, ["Password"] = password });
            Assert.Equal("/", switchedAccount.Headers.Location?.OriginalString);
            Assert.Equal(HttpStatusCode.Redirect, (await admin.GetAsync("/admin/users")).StatusCode);

            using var signedBackIn = await SubmitAsync(admin, "/account/sign-in",
                new() { ["Email"] = adminAccount.Email, ["Password"] = password });
            Assert.Equal("/staff/dashboard", signedBackIn.Headers.Location?.OriginalString);
            using var signedOut = await SubmitAsync(admin, "/account/sign-in", "/account/sign-out", []);
            Assert.Equal(HttpStatusCode.Redirect, signedOut.StatusCode);
            Assert.Contains(signedOut.Headers.GetValues("Set-Cookie"),
                header => header.StartsWith("__Host-HihStaffPin=;", StringComparison.Ordinal));
            Assert.Equal(HttpStatusCode.Redirect, (await admin.GetAsync("/admin/users")).StatusCode);
        }
        finally { await context.Client.DropDatabaseAsync(database); }

        async Task<AccountUser> CreateAsync(string email, string role)
        {
            var user = new AccountUser { Email = email, EmailConfirmed = true };
            Assert.True((await users.CreateAsync(user, password)).Succeeded);
            user.Roles = [role];
            Assert.True((await users.UpdateAsync(user)).Succeeded);
            return user;
        }

        async Task SignInAsync(HttpClient client, string email, string pin)
        {
            var target = (await users.FindByEmailAsync(email))!;
            var staffPins = scope.ServiceProvider.GetRequiredService<StaffPinService>();
            var staffAccess = scope.ServiceProvider.GetRequiredService<StaffPinAccess>();
            var pinHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<AccountUser>>();
            Assert.True(await staffPins.TrySaveAsync(target.Id, pinHasher.HashPassword(target, staffAccess.PinInput(target, pin)), target.SecurityStamp, default));
            Assert.Equal(HttpStatusCode.Redirect, (await SubmitAsync(client, "/staff/sign-in",
                new() { ["Email"] = email, ["Pin"] = pin })).StatusCode);
        }

        static Dictionary<string, string> Fields(string role, string version) =>
            new() { ["Form.Role"] = role, ["Form.Version"] = version, ["Form.Reason"] = "Roster change" };
    }

    private static string Token(string html)
    {
        var value = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        Assert.NotEmpty(value);
        return WebUtility.HtmlDecode(value);
    }

    internal static Task<HttpResponseMessage> SubmitAsync(HttpClient client, string path, Dictionary<string, string> fields) =>
        SubmitAsync(client, path, path, fields);

    private static async Task<HttpResponseMessage> SubmitAsync(HttpClient client, string formPath, string submitPath, Dictionary<string, string> fields)
    {
        fields["__RequestVerificationToken"] = Token(await client.GetStringAsync(formPath));
        return await client.PostAsync(submitPath, new FormUrlEncodedContent(fields));
    }
}
