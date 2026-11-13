using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
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

public sealed class StaffBlockHttpTests
{
    private sealed class Clock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 11, 10, 4, 15, 0, TimeSpan.Zero);
    }
    private sealed class TestAuth(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var role = Request.Headers["X-Test-Role"].ToString();
            return Task.FromResult(role.Length == 0 ? AuthenticateResult.NoResult() : AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity([
                    new Claim(ClaimTypes.NameIdentifier, "test-admin"), new Claim(ClaimTypes.Role, role)
                ], Scheme.Name)), Scheme.Name)));
        }
    }
    private static WebApplicationFactory<Program> Factory(string database) => new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        builder.UseEnvironment("Development").ConfigureLogging(logging => logging.ClearProviders())
        .ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Development:StaffWorkspaceUrl"] = "",
            ["MongoDb:ConnectionString"] = "mongodb://localhost:27017/?replicaSet=rs0",
            ["MongoDb:DatabaseName"] = database,
            ["MongoDb:ReplicaSetName"] = "rs0",
            ["Smtp:Host"] = ""
        }))
        .ConfigureTestServices(services =>
        {
            services.AddSingleton<TimeProvider>(new Clock());
            services.AddDataProtection().UseEphemeralDataProtectionProvider();
            services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, TestAuth>("BlockTest", _ => { });
            services.Configure<CookieAuthenticationOptions>(StaffPinAccess.Scheme, options => options.ForwardDefault = "BlockTest");
        }));
    private static HttpClient Client(WebApplicationFactory<Program> factory, string role)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        if (role.Length > 0) { client.DefaultRequestHeaders.Add("X-Test-Role", role); }
        return client;
    }

    [Theory]
    [InlineData("Staff", HttpStatusCode.Forbidden)]
    [InlineData("Customer", HttpStatusCode.Forbidden)]
    [InlineData("", HttpStatusCode.Unauthorized)]
    public async Task NonAdminsCannotReadOrSubmitBlocks(string role, HttpStatusCode expected)
    {
        await using var factory = Factory("test_block_access");
        using var client = Client(factory, role);
        Assert.Equal(role == "Staff" ? HttpStatusCode.BadRequest : expected, (await client.PostAsync("/staff/reservations/missing/attendance", new FormUrlEncodedContent([]))).StatusCode);
        Assert.Equal(expected, (await client.GetAsync("/staff/reservations/blocks/new")).StatusCode);
        Assert.Equal(expected, (await client.GetAsync("/staff/reservations/blocks/missing")).StatusCode);
        Assert.Equal(expected, (await client.PostAsync("/staff/reservations/blocks/missing/release", new FormUrlEncodedContent([]))).StatusCode);
        Assert.Equal(expected, (await client.PostAsync("/staff/reservations/blocks/new", new FormUrlEncodedContent([]))).StatusCode);
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("Manager")]
    public async Task VenueManagementCannotSubmitWithoutAntiforgeryToken(string role)
    {
        await using var factory = Factory("test_block_access");
        using var client = Client(factory, role);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/staff/reservations/missing/attendance", new FormUrlEncodedContent([]))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/staff/reservations/blocks/new", new FormUrlEncodedContent([]))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/staff/reservations/blocks/missing/release", new FormUrlEncodedContent([]))).StatusCode);
    }

    [Theory, Trait("Category", "Integration")]
    [InlineData("Admin")]
    [InlineData("Manager")]
    public async Task VenueManagementCanSaveReplayAndReleaseBlocks(string role)
    {
        var database = "test_block_http_" + Guid.NewGuid().ToString("N");
        await using var factory = Factory(database);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IMongoDbContext>();
        try
        {
            await new Migration_002_Reservations().UpAsync(context.Database, default);
            await new AccountsMigration().UpAsync(context.Database, default);
            await new Migration_004_BookingProcess().UpAsync(context.Database, default);
            await new Migration_005_GoogleAccounts().UpAsync(context.Database, default);
            await new Migration_006_SchemaHardening().UpAsync(context.Database, default);
            await new Migration_011_AdminScheduleBlocks().UpAsync(context.Database, default);
            await new Migration_012_ReleaseScheduleBlocks().UpAsync(context.Database, default);
            using var client = Client(factory, role);
            var html = await client.GetStringAsync("/staff/reservations/blocks/new?date=2026-11-10");
            string Value(string name) => WebUtility.HtmlDecode(Regex.Match(html, "name=\"" + name + "\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
            var form = new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = Value("__RequestVerificationToken"),
                ["IdempotencyKey"] = Value("IdempotencyKey"),
                ["ResourceId"] = "bay-2",
                ["Date"] = "2026-11-10",
                ["StartTime"] = "13:00",
                ["DurationMinutes"] = "60",
                ["Reason"] = "Simulator maintenance"
            };
            Assert.NotEmpty(form["__RequestVerificationToken"]);
            Assert.NotEmpty(form["IdempotencyKey"]);
            client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/staff/reservations/blocks/new", new FormUrlEncodedContent(form))).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/staff/reservations/blocks/new", new FormUrlEncodedContent(form))).StatusCode);
            var saved = await context.Database.GetCollection<ScheduleBlock>("schedule_blocks").Find(FilterDefinition<ScheduleBlock>.Empty).SingleAsync();
            Assert.Equal("test-admin", saved.CreatedBy);
            form["IdempotencyKey"] = "conflict";
            var conflict = await client.PostAsync("/staff/reservations/blocks/new", new FormUrlEncodedContent(form));
            Assert.Equal(HttpStatusCode.UnprocessableEntity, conflict.StatusCode);
            Assert.Contains("no longer free", await conflict.Content.ReadAsStringAsync());
            client.DefaultRequestHeaders.Add("X-Staff-Schedule", "true");
            var prefetched = await client.GetStringAsync("/BookingReview?date=2026-11-10");
            Assert.DoesNotContain("data-toast-seed", prefetched);
            client.DefaultRequestHeaders.Remove("X-Staff-Schedule");
            var schedule = await client.GetStringAsync("/BookingReview?date=2026-11-10");
            Assert.Contains("data-toast-seed data-kind=\"success\"", schedule);
            Assert.Contains(">Time blocked<", schedule);
            Assert.DoesNotContain("data-toast-seed", await client.GetStringAsync("/BookingReview?date=2026-11-10"));
            Assert.Contains("data-new-block", schedule);
            Assert.Contains("Simulator maintenance", schedule);
            Assert.Contains("block-reason", schedule);
            client.DefaultRequestHeaders.Accept.Clear();
            var fallback = await client.PostAsync("/staff/reservations/blocks/new", new FormUrlEncodedContent(form));
            var fallbackHtml = await fallback.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.OK, fallback.StatusCode);
            Assert.Contains("Simulator maintenance", fallbackHtml);
            Assert.Contains("no longer free", fallbackHtml);
            using var staff = Client(factory, "Staff");
            Assert.DoesNotContain("data-new-block", await staff.GetStringAsync("/BookingReview?date=2026-11-10"));
            var path = "/staff/reservations/blocks/" + Uri.EscapeDataString(saved.Id);
            Assert.Contains(path, schedule);
            Assert.DoesNotContain(path, await staff.GetStringAsync("/BookingReview?date=2026-11-10"));
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/staff/reservations/blocks/missing")).StatusCode);
            var details = await client.GetStringAsync(path);
            Assert.Contains("Simulator maintenance", details);
            Assert.Contains("Confirm unblock", details);
            var release = new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = WebUtility.HtmlDecode(Regex.Match(details, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value),
                ["Reason"] = "Maintenance finished",
                ["ConfirmRelease"] = "false"
            };
            var unconfirmed = await client.PostAsync(path + "/release", new FormUrlEncodedContent(release));
            Assert.Equal(HttpStatusCode.OK, unconfirmed.StatusCode);
            Assert.Contains("Confirm that", await unconfirmed.Content.ReadAsStringAsync());
            Assert.Null((await context.Database.GetCollection<ScheduleBlock>("schedule_blocks").Find(x => x.Id == saved.Id).SingleAsync()).ReleasedUtc);
            release["ConfirmRelease"] = "true";
            release["Reason"] = " ";
            var emptyReason = await client.PostAsync(path + "/release", new FormUrlEncodedContent(release));
            Assert.Contains("required", await emptyReason.Content.ReadAsStringAsync());
            release["Reason"] = "Maintenance finished";
            var released = await client.PostAsync(path + "/release", new FormUrlEncodedContent(release));
            Assert.Equal(HttpStatusCode.Redirect, released.StatusCode);
            Assert.Contains(saved.Id, Uri.UnescapeDataString(released.Headers.Location!.ToString()));
            release["Reason"] = "A different retry";
            Assert.Equal(HttpStatusCode.Redirect, (await client.PostAsync(path + "/release", new FormUrlEncodedContent(release))).StatusCode);
            var history = await client.GetStringAsync(path);
            Assert.Contains("Time unblocked", history);
            Assert.Contains("data-toast-seed data-kind=\"info\"", history);
            Assert.Contains(">Time block already released<", history);
            Assert.Contains("Maintenance finished", history);
            Assert.DoesNotContain("A different retry", history);
            Assert.DoesNotContain("Confirm unblock", history);
            Assert.DoesNotContain(path, await client.GetStringAsync("/BookingReview?date=2026-11-10"));
        }
        finally { await context.Client.DropDatabaseAsync(database); }
    }
    [Theory, Trait("Category", "Integration")]
    [InlineData("changes", "approve", "Schedule change approved", "/staff/review-queue/changes", "12 Nov, 2:00 PM")]
    [InlineData("changes", "decline", "Schedule change declined", "/staff/review-queue/changes", "Original time kept: 11 Nov, 12:00 PM")]
    [InlineData("cancellations", "cancel", "Cancellation approved", "/staff/cancellations", "reserved bay time was released")]
    [InlineData("cancellations", "keep", "Cancellation declined", "/staff/cancellations", "reservation remains scheduled")]
    public async Task QuickReviewDecisionsReturnToTheFilteredListWithOneToast(
        string section, string decision, string title, string destinationPath, string message)
    {
        var database = "test_review_toast_" + Guid.NewGuid().ToString("N");
        await using var factory = Factory(database);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IMongoDbContext>();
        try
        {
            await new Migration_002_Reservations().UpAsync(context.Database, default);
            await new AccountsMigration().UpAsync(context.Database, default);
            await new Migration_004_BookingProcess().UpAsync(context.Database, default);
            await new Migration_005_GoogleAccounts().UpAsync(context.Database, default);
            await new Migration_006_SchemaHardening().UpAsync(context.Database, default);
            await new Migration_015_ScheduleChanges().UpAsync(context.Database, default);
            var start = new DateTime(2026, 11, 11, 4, 0, 0, DateTimeKind.Utc);
            var booking = new ReservationRecord
            {
                AccountId = "customer",
                Email = "guest@example.test",
                Reference = "HIH-REVIEW-TOAST",
                FirstName = "Alex",
                LastName = "Santos",
                Service = "Regular",
                Status = "Confirmed",
                ResourceIds = ["bay-2"],
                Attendees = 1,
                StartUtc = start,
                EndUtc = start.AddHours(1),
                CreatedUtc = start.AddDays(-2),
                ReviewDeadlineUtc = start.AddDays(-1),
                IdempotencyKey = "toast",
                SubmissionHash = "test",
                CancellationRequested = section == "cancellations"
            };
            if (section == "changes")
            {
                booking.ScheduleChange = new ScheduleChangeRecord
                {
                    Id = Guid.NewGuid().ToString("N"),
                    PayloadHash = new string('a', 64),
                    RequestedUtc = start.AddDays(-1),
                    Status = "Pending",
                    StartUtc = start.AddDays(1).AddHours(2),
                    EndUtc = start.AddDays(1).AddHours(3),
                    OriginalStartUtc = start,
                    OriginalEndUtc = booking.EndUtc,
                    OriginalResourceIds = booking.ResourceIds,
                    ResourceIds = booking.ResourceIds,
                    Reason = "Move our visit."
                };
            }
            await context.Database.GetCollection<ReservationRecord>("reservations").InsertOneAsync(booking);
            using var admin = Client(factory, "Admin");
            var actions = await admin.GetStringAsync($"/staff/reservations/{booking.Id}/actions?filter={section}&day=2026-11-11&sort=session");
            var formAction = WebUtility.HtmlDecode(Regex.Match(actions, "action=\"([^\"]*?/Decide(?:Change)?[^\"]*)\"").Groups[1].Value);
            Assert.Contains("returnToQueue=true", formAction, StringComparison.OrdinalIgnoreCase);
            var token = WebUtility.HtmlDecode(Regex.Match(actions, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
            var form = new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token,
                ["expectedRevision"] = "0",
                ["decision"] = decision,
                ["reason"] = "Reviewed with the guest.",
                ["proposalId"] = booking.ScheduleChange?.Id ?? "",
                ["resources"] = "bay-2"
            };
            var response = await admin.PostAsync(formAction, new FormUrlEncodedContent(form));
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            var destination = new Uri(new Uri("https://localhost"), response.Headers.Location!);
            Assert.Equal(destinationPath, destination.AbsolutePath);
            Assert.Contains("day=2026-11-11", destination.Query);
            Assert.Contains("sort=session", destination.Query);
            var page = await admin.GetStringAsync(destination);
            Assert.Contains("data-toast-seed data-kind=\"success\"", page);
            Assert.Contains(">" + title + "<", page);
            Assert.Contains(message, WebUtility.HtmlDecode(page));
            Assert.DoesNotContain("data-toast-action aria-label=", page);
            Assert.DoesNotContain("data-toast-seed", await admin.GetStringAsync(destination));

            var replay = await admin.PostAsync(formAction, new FormUrlEncodedContent(form));
            Assert.Equal(HttpStatusCode.Redirect, replay.StatusCode);
            Assert.Contains("data-toast-seed data-kind=\"warning\"", await admin.GetStringAsync(replay.Headers.Location));
        }
        finally { await context.Client.DropDatabaseAsync(database); }
    }

    [Fact, Trait("Category", "Integration")]
    public async Task StaffAttendanceRequiresConfirmationAndPreservesActualTimesAndRetries()
    {
        var database = "test_attendance_http_" + Guid.NewGuid().ToString("N");
        await using var factory = Factory(database);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IMongoDbContext>();
        try
        {
            await new Migration_002_Reservations().UpAsync(context.Database, default);
            await new AccountsMigration().UpAsync(context.Database, default);
            await new Migration_004_BookingProcess().UpAsync(context.Database, default);
            await new Migration_005_GoogleAccounts().UpAsync(context.Database, default);
            await new Migration_006_SchemaHardening().UpAsync(context.Database, default);
            await new Migration_013_ReservationAttendance().UpAsync(context.Database, default);
            var start = new DateTime(2026, 11, 10, 4, 0, 0, DateTimeKind.Utc);
            var booking = new ReservationRecord
            {
                AccountId = "customer",
                Email = "customer@example.test",
                Reference = "HIH-ATTENDANCE",
                FirstName = "Alex",
                LastName = "Santos",
                Service = "Regular",
                Status = "Confirmed",
                ResourceIds = ["bay-2"],
                StartUtc = start,
                EndUtc = start.AddHours(2),
                CreatedUtc = start.AddDays(-1),
                ReviewDeadlineUtc = start,
                IdempotencyKey = "attendance",
                SubmissionHash = "test",
                Attendees = 1
            };
            await context.Database.GetCollection<ReservationRecord>("reservations").InsertOneAsync(booking);
            using var staff = Client(factory, "Staff");
            var path = "/staff/reservations/" + booking.Id + "/attendance";
            Assert.Equal(HttpStatusCode.BadRequest, (await staff.PostAsync(path, new FormUrlEncodedContent([]))).StatusCode);
            var details = await staff.GetStringAsync("/BookingReview/Details/" + booking.Id);
            string Value(string html, string name) => WebUtility.HtmlDecode(Regex.Match(html, "name=\"" + name + "\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
            var form = new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = Value(details, "__RequestVerificationToken"),
                ["Action"] = "check-in",
                ["CommandId"] = Value(details, "CommandId"),
                ["ExpectedRevision"] = "0",
                ["ConfirmAttendance"] = "false",
                ["Actor"] = "forged",
                ["CheckedInUtc"] = "2000-01-01",
                ["Status"] = "Completed"
            };
            Assert.NotEmpty(form["CommandId"]);
            var uncheckedResponse = await staff.PostAsync(path, new FormUrlEncodedContent(form));
            Assert.Equal(HttpStatusCode.OK, uncheckedResponse.StatusCode);
            Assert.Contains("Confirm the attendance", await uncheckedResponse.Content.ReadAsStringAsync());
            form["ConfirmAttendance"] = "true";
            form["Action"] = "forged-action";
            Assert.Equal(HttpStatusCode.OK, (await staff.PostAsync(path, new FormUrlEncodedContent(form))).StatusCode);
            form["Action"] = "check-in";
            var checkedIn = await staff.PostAsync(path, new FormUrlEncodedContent(form));
            Assert.Equal(HttpStatusCode.Redirect, checkedIn.StatusCode);
            var checkedInPage = await staff.GetStringAsync(checkedIn.Headers.Location);
            Assert.Contains("data-toast-seed data-kind=\"success\"", checkedInPage);
            Assert.Contains(">Guest checked in<", checkedInPage);
            Assert.Equal(HttpStatusCode.Redirect, (await staff.PostAsync(path, new FormUrlEncodedContent(form))).StatusCode);
            var current = await context.Database.GetCollection<ReservationRecord>("reservations").Find(x => x.Id == booking.Id).SingleAsync();
            Assert.Equal("Checked-In", current.Status); Assert.Equal(1, current.Revision);
            Assert.Equal("test-admin", Assert.Single(current.History).Actor);
            Assert.Equal(new DateTime(2026, 11, 10, 4, 15, 0, DateTimeKind.Utc), current.CheckedInUtc);
            details = await staff.GetStringAsync("/BookingReview/Details/" + booking.Id);
            Assert.Contains(">Guest already checked in<", details);
            form["Action"] = "check-out"; form["CommandId"] = Value(details, "CommandId"); form["ExpectedRevision"] = "1";
            Assert.Contains("name=\"Action\" value=\"check-out\"", details);
            Assert.Contains("name=\"ConfirmAttendance\" value=\"true\" required", details);
            Assert.Contains("Guest finished and all booked bays are clear.", details);
            Assert.Contains(">Check out guest</button>", details);
            var noReason = await staff.PostAsync(path, new FormUrlEncodedContent(form));
            Assert.Equal(HttpStatusCode.OK, noReason.StatusCode);
            Assert.Contains("reason for early check-out", await noReason.Content.ReadAsStringAsync());
            form["Reason"] = "Finished early";
            var checkedOut = await staff.PostAsync(path, new FormUrlEncodedContent(form));
            Assert.Equal(HttpStatusCode.Redirect, checkedOut.StatusCode);
            var checkedOutPage = await staff.GetStringAsync(checkedOut.Headers.Location);
            Assert.Contains("data-toast-seed data-kind=\"success\"", checkedOutPage);
            Assert.Contains(">Guest checked out<", checkedOutPage);
            Assert.Equal(HttpStatusCode.Redirect, (await staff.PostAsync(path, new FormUrlEncodedContent(form))).StatusCode);
            var replayPage = await staff.GetStringAsync("/BookingReview/Details/" + booking.Id);
            Assert.Contains("data-toast-seed data-kind=\"info\"", replayPage);
            Assert.Contains(">Guest already checked out<", replayPage);
            current = await context.Database.GetCollection<ReservationRecord>("reservations").Find(x => x.Id == booking.Id).SingleAsync();
            Assert.Equal("Completed", current.Status); Assert.Equal(2, current.History.Count);
            Assert.Equal(booking.EndUtc, current.EndUtc);
            var schedule = await staff.GetStringAsync("/staff?date=2026-11-10");
            Assert.Contains("HIH-ATTENDANCE", schedule);
            Assert.Contains("data-status=\"Completed\"", schedule);
            Assert.DoesNotContain("data-holds-capacity=\"true\"", schedule);
        }
        finally { await context.Client.DropDatabaseAsync(database); }
    }

    [Theory]
    [InlineData("Staff")]
    [InlineData("Manager")]
    [InlineData("Customer")]
    public async Task OnlyAdminsCanManageResourceInventory(string role)
    {
        await using var factory = Factory("test_resource_access");
        using var client = Client(factory, role);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/admin/resources")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync("/admin/resources", new FormUrlEncodedContent([]))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync("/admin/resources/active", new FormUrlEncodedContent([]))).StatusCode);
    }

    [Fact, Trait("Category", "Integration")]
    public async Task ResourceActionsSupportModalsToastsAndReservationConflicts()
    {
        var database = "test_resource_http_" + Guid.NewGuid().ToString("N");
        await using var factory = Factory(database);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IMongoDbContext>();
        try
        {
            await new Migration_002_Reservations().UpAsync(context.Database, default);
            using var client = Client(factory, "Admin");
            var html = await client.GetStringAsync("/admin/resources");
            Assert.Contains("data-resource-add-dialog", html);
            Assert.Contains("data-resource-confirm", html);
            var token = WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
            Assert.NotEmpty(token);
            client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
            var form = new Dictionary<string, string> { ["__RequestVerificationToken"] = token, ["id"] = "test-bay", ["name"] = "Test bay", ["service"] = "VIP" };
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/admin/resources", new FormUrlEncodedContent(new Dictionary<string, string> { ["id"] = "no-token" }))).StatusCode);
            var added = await client.PostAsync("/admin/resources", new FormUrlEncodedContent(form));
            Assert.Equal(HttpStatusCode.OK, added.StatusCode);
            Assert.Contains("/admin/resources", await added.Content.ReadAsStringAsync());
            Assert.Contains("Bay added", await client.GetStringAsync("/admin/resources"));
            var duplicate = await client.PostAsync("/admin/resources", new FormUrlEncodedContent(form));
            Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
            Assert.Contains("already exists", await duplicate.Content.ReadAsStringAsync());
            var resources = context.Database.GetCollection<ResourceRecord>("resources");
            foreach (var active in new[] { "false", "true" })
            {
                form["active"] = active;
                Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/admin/resources/active", new FormUrlEncodedContent(form))).StatusCode);
                Assert.Equal(bool.Parse(active), (await resources.Find(x => x.Id == "test-bay").SingleAsync()).Active);
                Assert.Contains(active == "true" ? "Bay restored" : "Bay retired", await client.GetStringAsync("/admin/resources"));
            }
            await context.Database.GetCollection<ReservationRecord>("reservations").InsertOneAsync(new ReservationRecord
            {
                AccountId = "resource-test", Email = "resource@example.test", Status = "Confirmed", Service = "VIP",
                StartUtc = new DateTime(2026, 11, 11, 4, 0, 0, DateTimeKind.Utc), EndUtc = new DateTime(2026, 11, 11, 5, 0, 0, DateTimeKind.Utc),
                ResourceIds = ["test-bay"], IdempotencyKey = "resource-test", SubmissionHash = "resource-test"
            });
            form["active"] = "false";
            var blocked = await client.PostAsync("/admin/resources/active", new FormUrlEncodedContent(form));
            Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
            Assert.Contains("Resolve current or upcoming", await blocked.Content.ReadAsStringAsync());
            Assert.True((await resources.Find(x => x.Id == "test-bay").SingleAsync()).Active);
        }
        finally { await context.Client.DropDatabaseAsync(database); }
    }
    [Theory]
    [InlineData("Staff")]
    [InlineData("Customer")]
    public async Task DashboardTrendsCannotBeReadWithoutVenueManagement(string role)
    {
        await using var factory = Factory("test_dashboard_access");
        using var client = Client(factory, role);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/staff/dashboard?mode=trends")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/staff/dashboard/bookings?mode=trends&kind=all")).StatusCode);
    }

    [Theory]
    [InlineData("/staff/dashboard?mode=invalid")]
    [InlineData("/staff/dashboard?mode=trends&from=2026-01-01&to=2026-12-31")]
    [InlineData("/staff/dashboard?date=not-a-date")]
    [InlineData("/staff/dashboard/bookings?kind=hour&value=25")]
    [InlineData("/staff/dashboard/bookings?kind=day&value=2020-01-01")]
    [InlineData("/staff/dashboard/bookings?page=0")]
    public async Task DashboardRejectsInvalidFiltersBeforeQuerying(string path)
    {
        await using var factory = Factory("test_dashboard_invalid");
        using var client = Client(factory, "Admin");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(path)).StatusCode);
    }

    [Fact, Trait("Category", "Integration")]
    public async Task DashboardTotalsReconcileWithSlicesAndDoNotExposeContactFields()
    {
        var database = "test_dashboard_http_" + Guid.NewGuid().ToString("N");
        await using var factory = Factory(database);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IMongoDbContext>();
        try
        {
            await new Migration_002_Reservations().UpAsync(context.Database, default);
            foreach (var collection in new[] { "customer_profiles", "customer_profile_operations", "staff_role_audit" })
            {
                await context.Database.CreateCollectionAsync(collection);
            }
            var start = new DateTime(2026, 11, 10, 5, 0, 0, DateTimeKind.Utc);
            var bookings = context.Database.GetCollection<ReservationRecord>("reservations");
            await bookings.InsertManyAsync(Enumerable.Range(0, 23).Select(i => new ReservationRecord
            {
                AccountId = "dashboard-" + i, IdempotencyKey = "dashboard-" + i, SubmissionHash = "hash",
                Reference = "HIH-DASH-" + i, FirstName = "Example", LastName = "Guest", Email = "private@example.test", Phone = "09179999999",
                StartUtc = start, EndUtc = start.AddHours(2), Status = i == 22 ? "Cancelled" : "Confirmed", Service = "Regular",
                History = [new HistoryRecord { AtUtc = start.AddHours(-2).AddMinutes(i), Actor = "System", Status = i == 22 ? "Cancelled" : "Confirmed", EventType = i == 22 ? "ReservationCancelled" : "ReservationConfirmed" }],
                ResourceIds = ["bay-2", "bay-3"]
            }));
            using var client = Client(factory, "Admin");
            var html = await client.GetStringAsync("/staff/dashboard?date=2026-11-10");
            Assert.Contains("data-dashboard-data", html);
            Assert.Contains("data-dashboard-dialog", html);
            Assert.Contains("\"Hours\":88", html);
            Assert.DoesNotContain("private@example.test", html);
            Assert.DoesNotContain("09179999999", html);
            var slice = await client.GetStringAsync("/staff/dashboard/bookings?date=2026-11-10&kind=all");
            Assert.Contains("Page 1", slice); Assert.Contains(">Next<", slice);
            var page2 = await client.GetStringAsync("/staff/dashboard/bookings?date=2026-11-10&kind=all&page=2");
            Assert.DoesNotContain("Cancelled", page2); Assert.Contains("Page 2", page2);
            var trend = await client.GetStringAsync("/staff/dashboard?mode=trends&from=2026-11-10&to=2026-11-10");
            Assert.Contains("\"Booked\":22", trend); Assert.Contains("\"Other\":1", trend);
            client.DefaultRequestHeaders.Add("X-Dashboard", "true");
            var fragment = await client.GetStringAsync("/staff/dashboard?date=2026-11-10");
            Assert.DoesNotContain("<!DOCTYPE", fragment);
            Assert.Contains("data-dashboard", fragment);
        }
        finally { await context.Client.DropDatabaseAsync(database); }
    }
    [Fact]
    public void WorkspaceSignalsAreCoalescedAndDoNotCarryRecordData()
    {
        var updates = new HoleInHub.Web.Live.WorkspaceUpdates();
        var first = updates.Subscribe(); var second = updates.Subscribe();
        Assert.True(updates.HasSubscribers);
        updates.Publish(); updates.Publish();
        Assert.True(first.Reader.TryRead(out var changed)); Assert.True(changed);
        Assert.False(first.Reader.TryRead(out _)); Assert.True(second.Reader.TryRead(out _));
        updates.Unsubscribe(first.Id); updates.Unsubscribe(second.Id);
        Assert.False(updates.HasSubscribers);
    }

    [Theory]
    [InlineData("Customer", HttpStatusCode.Forbidden)]
    [InlineData("", HttpStatusCode.Unauthorized)]
    public async Task LiveStreamRequiresStaffAuthorization(string role, HttpStatusCode expected)
    {
        await using var factory = Factory("test_live_access");
        using var client = Client(factory, role);
        Assert.Equal(expected, (await client.GetAsync("/staff/live")).StatusCode);
    }

    [Fact]
    public async Task AuthorizedLiveStreamDeliversPublishedSignals()
    {
        await using var factory = Factory("test_live_stream");
        using var client = Client(factory, "Admin");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Get, "/staff/live"), HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(timeout.Token));
        Assert.Equal("retry: 1000", await reader.ReadLineAsync(timeout.Token));
        Assert.Equal("event: change", await reader.ReadLineAsync(timeout.Token));
        Assert.Equal("data: {}", await reader.ReadLineAsync(timeout.Token));
        Assert.Equal("", await reader.ReadLineAsync(timeout.Token));
        factory.Services.GetRequiredService<HoleInHub.Web.Live.WorkspaceUpdates>().Publish();
        Assert.Equal("event: change", await reader.ReadLineAsync(timeout.Token));
        Assert.Equal("data: {}", await reader.ReadLineAsync(timeout.Token));
        timeout.Cancel();
    }
}
