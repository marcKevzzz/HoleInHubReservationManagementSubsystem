using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using HoleInHub.Business.Reservations;
using HoleInHub.Data.Configuration;
using HoleInHub.Data.Context;
using HoleInHub.Data.Entities;
using HoleInHub.Data.Migrations.Definitions;
using HoleInHub.Data.Reservations;
using HoleInHub.Web.Accounts;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Xunit;

namespace HoleInHub.Tests.Web.Smoke;

[Trait("Category", "Integration")]
public sealed class GuestBookingFlowTests
{
    private sealed class SwitchableContext(IMongoDbContext inner) : IMongoDbContext
    {
        public bool Offline { get; set; }
        public int Reads { get; private set; }
        public int? FailOnRead { get; set; }
        private void Check() { Reads++; if (Offline || Reads == FailOnRead) { throw new TimeoutException("sensitive database details"); } }
        public IMongoClient Client { get { Check(); return inner.Client; } }
        public IMongoDatabase Database { get { Check(); return inner.Database; } }
        public IMongoCollection<MigrationRecord> Migrations => inner.Migrations;
        public IMongoCollection<MigrationLease> MigrationLeases => inner.MigrationLeases;
        public IMongoCollection<SystemHeartbeat> SystemHeartbeats => inner.SystemHeartbeats;
        public Task<bool> PingAsync(CancellationToken ct = default) => Offline ? Task.FromResult(false) : inner.PingAsync(ct);
    }
    private static readonly DateTimeOffset TestNow = new(DateTime.UtcNow.Date.AddDays(1).AddHours(4), TimeSpan.Zero);
    private sealed class BookingClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => TestNow;
    }
    private static WebApplicationFactory<Program> Factory(string database, string environment = "Development") => new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        builder.UseEnvironment(environment).ConfigureLogging(logging => logging.ClearProviders())
        .ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["MongoDb:ConnectionString"] = "mongodb://localhost:27017/?replicaSet=rs0",
            ["MongoDb:DatabaseName"] = database,
            ["MongoDb:ReplicaSetName"] = "rs0",
            ["ReservationPolicy:Approved"] = "true",
            ["ReservationPolicy:DurationsApproved"] = "true",
            ["ReservationPolicy:ReviewStart"] = "00:00:00",
            ["ReservationPolicy:ReviewEnd"] = "1.00:00:00",
            ["ReservationPolicy:ReviewDays:0"] = "0",
            ["ReservationPolicy:ReviewDays:1"] = "1",
            ["ReservationPolicy:ReviewDays:2"] = "2",
            ["ReservationPolicy:ReviewDays:3"] = "3",
            ["ReservationPolicy:ReviewDays:4"] = "4",
            ["ReservationPolicy:ReviewDays:5"] = "5",
            ["ReservationPolicy:ReviewDays:6"] = "6",
            ["ReservationPolicy:StaffEmails:0"] = "staff@example.test",
            ["ReservationPolicy:DemoPaymentsEnabled"] = "true",
            ["AccountEmail:PublicBaseUrl"] = "https://localhost",
            ["Smtp:Host"] = ""
        })).ConfigureServices(services => services.AddSingleton<TimeProvider>(new BookingClock())));
    private static HttpClient Client(WebApplicationFactory<Program> factory) => factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
    private static async Task SignInStaffAsync(HttpClient client, string email, IServiceProvider services)
    {
        client.DefaultRequestHeaders.Add("X-Test-Desktop", "approved");
        const string pin = "048217";
        var users = services.GetRequiredService<UserManager<AccountUser>>();
        var staff = (await users.FindByEmailAsync(email))!;
        var access = services.GetRequiredService<StaffPinAccess>();
        var hasher = services.GetRequiredService<IPasswordHasher<AccountUser>>();
        var pins = services.GetRequiredService<HoleInHub.Business.Accounts.StaffPinService>();
        Assert.True(await pins.TrySaveAsync(staff.Id, hasher.HashPassword(staff, access.PinInput(staff, pin)), staff.SecurityStamp, default));
        using var signIn = await StaffPinAccessTests.SubmitAsync(client, "/staff/sign-in",
            new() { ["Pin"] = pin });
        Assert.Equal(HttpStatusCode.Redirect, signIn.StatusCode);
    }
    private static string Csrf(string html) => WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
    private static async Task Seed(IMongoDbContext context)
    {
        await new AccountsMigration().UpAsync(context.Database, default);
        await new Migration_002_Reservations().UpAsync(context.Database, default);
        await new Migration_004_BookingProcess().UpAsync(context.Database, default);
        await new Migration_005_GoogleAccounts().UpAsync(context.Database, default);
        await new Migration_006_SchemaHardening().UpAsync(context.Database, default);
    }
    private static Dictionary<string, string> Form(string html, string service = "Regular") => new()
    {
        ["__RequestVerificationToken"] = Csrf(html),
        ["Service"] = service,
        ["Date"] = TestNow.AddDays(1).ToString("yyyy-MM-dd"),
        ["StartTime"] = service == "WholeShop" ? "15:00" : "14:00",
        ["DurationMinutes"] = service == "WholeShop" ? "360" : "60",
        ["ResourceIds"] = "bay-2",
        ["FirstName"] = "Guest",
        ["LastName"] = "Player",
        ["Phone"] = "09123456789",
        ["Email"] = "guest@example.test",
        ["PrivacyAccepted"] = "true",
        ["IdempotencyKey"] = Guid.NewGuid().ToString("N"),
        ["Attendees"] = "3"
    };
    private static async Task<HttpResponseMessage> Verify(HttpClient client, IMongoDbContext context, IServiceProvider services, Dictionary<string, string> form)
    {
        var sent = await client.PostAsync("/Reservations/Create", new FormUrlEncodedContent(form));
        Assert.Equal(HttpStatusCode.Redirect, sent.StatusCode);
        Assert.Equal("/Reservations/Verify", sent.Headers.Location!.ToString());
        Assert.Equal(0, await context.Database.GetCollection<ReservationRecord>("reservations").CountDocumentsAsync(FilterDefinition<ReservationRecord>.Empty));
        var page = await client.GetStringAsync(sent.Headers.Location);
        Assert.Contains("verification-shell", page);
        Assert.DoesNotContain("site-header", page);
        Assert.DoesNotContain("site-footer", page);
        Assert.Contains("autocomplete=\"one-time-code\"", page);
        var email = await context.Database.GetCollection<OutboxRecord>("outbox").Find(x => x.Subject == "Your booking verification code").SingleAsync();
        Assert.StartsWith("protected:", email.Body);
        var plain = services.GetRequiredService<IDataProtectionProvider>().CreateProtector("BookingEmail").Unprotect(email.Body[10..]);
        var code = Regex.Match(plain, "[0-9]{6}").Value;
        var challenge = await context.Database.GetCollection<GuestVerificationRecord>("guest_verifications").Find(FilterDefinition<GuestVerificationRecord>.Empty).SingleAsync();
        Assert.NotEqual(code, challenge.CodeHash); Assert.NotNull(challenge.ProtectedContext);
        var wrong = await client.PostAsync("/Reservations/Verify", new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = Csrf(page), ["code"] = "000000" }));
        Assert.Equal(HttpStatusCode.OK, wrong.StatusCode);
        Assert.Equal(0, await context.Database.GetCollection<ReservationRecord>("reservations").CountDocumentsAsync(FilterDefinition<ReservationRecord>.Empty));
        var verified = await client.PostAsync("/Reservations/Verify", new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = Csrf(await wrong.Content.ReadAsStringAsync()), ["code"] = code }));
        Assert.Equal(HttpStatusCode.Redirect, verified.StatusCode);
        Assert.Contains("/Reservations/Confirmation/", verified.Headers.Location!.ToString());
        return verified;
    }
    [Theory]
    [InlineData("Attendees", "invalid", "Attendees")]
    [InlineData("FirstName", "Guest2", "FirstName")]
    [InlineData("LastName", "Player٣", "LastName")]
    [InlineData("Phone", "+639123456789", "Phone")]
    [InlineData("Phone", "0912abc6789", "Phone")]
    [InlineData("StartTime", "01:00", "")]
    public async Task FailedSubmissionProvidesFieldKeysForWizardFeedback(string field, string value, string expectedKey)
    {
        var database = "hih_feedback_" + Guid.NewGuid().ToString("N"); await using var factory = Factory(database);
        using var scope = factory.Services.CreateScope(); var context = scope.ServiceProvider.GetRequiredService<IMongoDbContext>();
        try
        {
            await Seed(context); using var client = Client(factory);
            var form = Form(await client.GetStringAsync("/Reservations/Create")); form[field] = value;
            var response = await client.PostAsync("/Reservations/Create", new FormUrlEncodedContent(form));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var html = await response.Content.ReadAsStringAsync();
            var keys = JsonSerializer.Deserialize<string[]>(WebUtility.HtmlDecode(Regex.Match(html, "data-error-fields=\"([^\"]*)\"").Groups[1].Value));
            Assert.Contains(expectedKey, keys!);
            Assert.Contains("validation-summary-errors", html);
            Assert.Equal(0, await context.Database.GetCollection<ReservationRecord>("reservations").CountDocumentsAsync(FilterDefinition<ReservationRecord>.Empty));
        }
        finally { await context.Client.DropDatabaseAsync(database); }
    }
    [Fact]
    public async Task SubmitStartsVerificationWithoutCreatingCapacityHold()
    {
        var database = "hih_submit_verify_" + Guid.NewGuid().ToString("N"); await using var factory = Factory(database);
        using var scope = factory.Services.CreateScope(); var context = scope.ServiceProvider.GetRequiredService<IMongoDbContext>();
        try
        {
            await Seed(context); using var client = Client(factory);
            var form = Form(await client.GetStringAsync("/Reservations/Create")); form["Attendees"] = "3";
            var submitted = await client.PostAsync("/Reservations/Create", new FormUrlEncodedContent(form));
            Assert.Equal(HttpStatusCode.Redirect, submitted.StatusCode);
            Assert.Equal("/Reservations/Verify", submitted.Headers.Location!.ToString());
            Assert.Equal(0, await context.Database.GetCollection<ReservationRecord>("reservations").CountDocumentsAsync(FilterDefinition<ReservationRecord>.Empty));
        }
        finally { await context.Client.DropDatabaseAsync(database); }
    }
    [Fact]
    public async Task GuestCanVerifySubmitTrackAndRequestCancellationWithoutJavaScript()
    {
        var database = "hih_guest_flow_" + Guid.NewGuid().ToString("N"); await using var factory = Factory(database);
        using var scope = factory.Services.CreateScope(); var context = scope.ServiceProvider.GetRequiredService<IMongoDbContext>();
        try
        {
            await Seed(context); using var client = Client(factory); var html = await client.GetStringAsync("/Reservations/Create"); var form = Form(html); form["DurationMinutes"] = "30";
            var submitted = await Verify(client, context, scope.ServiceProvider, form); Assert.Equal(HttpStatusCode.Redirect, submitted.StatusCode);
            var link = submitted.Headers.Location!.ToString(); var status = await client.GetStringAsync(link); Assert.Contains("Pending", status);
            var saved = await context.Database.GetCollection<ReservationRecord>("reservations").Find(FilterDefinition<ReservationRecord>.Empty).SingleAsync();
            Assert.Equal(3, saved.Attendees); Assert.Empty(saved.Participants); Assert.Equal(30, (saved.ReviewDeadlineUtc - saved.CreatedUtc).TotalMinutes);
            var retry = await client.PostAsync("/Reservations/Create", new FormUrlEncodedContent(form));
            Assert.Equal(HttpStatusCode.Redirect, retry.StatusCode); Assert.Equal(link, retry.Headers.Location!.ToString());
            Assert.Equal(1, await context.Database.GetCollection<ReservationRecord>("reservations").CountDocumentsAsync(FilterDefinition<ReservationRecord>.Empty));
            using var stranger = Client(factory); Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync("/Reservations/Status/" + saved.Id)).StatusCode);
            Assert.Equal(HttpStatusCode.Redirect, (await stranger.GetAsync("/BookingReview")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/Reservations/RequestCancellation/" + saved.Id, new FormUrlEncodedContent(new Dictionary<string, string>()))).StatusCode);
            var token = WebUtility.UrlDecode(link.Split("token=")[1]);
            status = await client.GetStringAsync("/Reservations/Status/" + saved.Id + "?token=" + token);
            var cancellation = await client.PostAsync("/Reservations/RequestCancellation/" + saved.Id, new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = Csrf(status), ["token"] = token }));
            Assert.Equal(HttpStatusCode.Redirect, cancellation.StatusCode);
            Assert.Equal("Cancelled", (await scope.ServiceProvider.GetRequiredService<ReservationService>().GetStaffAsync(saved.Id, default))!.Status);
            var email = await context.Database.GetCollection<OutboxRecord>("outbox").Find(x => x.Id == saved.Id + ":submitted").SingleAsync();
            var receipt = scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>().CreateProtector("BookingEmail").Unprotect(email.Body[10..]);
            Assert.Contains("Reservation status: Pending", receipt);
            Assert.Contains("Number of guests: 3", receipt);
            Assert.Contains("Asia/Manila", receipt);
            var emailedPath = Regex.Match(receipt, "https://localhost([^ ]+)").Groups[1].Value;
            Assert.Contains(saved.Reference, await stranger.GetStringAsync(emailedPath));
        }
        finally { await context.Client.DropDatabaseAsync(database); }
    }
    [Fact]
    public async Task WholeShopDemoCheckoutConfirmsAndUsesServerQuote()
    {
        var database = "hih_checkout_" + Guid.NewGuid().ToString("N"); await using var factory = Factory(database);
        using var scope = factory.Services.CreateScope(); var context = scope.ServiceProvider.GetRequiredService<IMongoDbContext>();
        try
        {
            await Seed(context); using var client = Client(factory); var form = Form(await client.GetStringAsync("/Reservations/Create"), "WholeShop");
            form["RentalTotal"] = "1"; form["DepositAmount"] = "0";
            var submitted = await Verify(client, context, scope.ServiceProvider, form); Assert.Equal(HttpStatusCode.Redirect, submitted.StatusCode);
            var confirmation = await client.GetStringAsync(submitted.Headers.Location);
            Assert.Contains("id=\"receipt-status\">Reservation status</h2>", confirmation);
            Assert.Contains("class=\"booking-current-status\">Pending</p>", confirmation);
            var location = WebUtility.HtmlDecode(Regex.Match(confirmation, "action=\"([^\"]*Checkout[^\"]*)\"").Groups[1].Value);
            Assert.Contains("No real money", confirmation);
            Assert.DoesNotContain("site-header", confirmation);
            Assert.DoesNotContain("site-footer", confirmation);
            var token = WebUtility.HtmlDecode(Regex.Match(confirmation, "name=\"token\" value=\"([^\"]*)\"").Groups[1].Value);
            var paid = await client.PostAsync(location, new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = Csrf(confirmation), ["token"] = token, ["outcome"] = "success" })); Assert.Equal(HttpStatusCode.Redirect, paid.StatusCode);
            var receipt = await client.GetStringAsync(paid.Headers.Location);
            Assert.Contains("RESERVATION CONFIRMED", receipt);
            Assert.DoesNotContain("site-footer", receipt);
            var saved = await context.Database.GetCollection<ReservationRecord>("reservations").Find(FilterDefinition<ReservationRecord>.Empty).SingleAsync();
            Assert.Equal("Confirmed", saved.Status); Assert.Equal("DepositReceived", saved.PaymentStatus); var quote = BookingRules.DemoRentalTotal(DateOnly.Parse(form["Date"]), 360); Assert.Equal(quote, saved.RentalTotal); Assert.Equal(quote / 2, saved.DepositAmount); Assert.Equal(7, saved.ResourceIds.Length);
        }
        finally { await context.Client.DropDatabaseAsync(database); }
    }
    [Fact]
    public async Task SubmittedReceiptIsAcceptedByLocalSmtpAfterDecryption()
    {
        var database = "hih_smtp_acceptance_" + Guid.NewGuid().ToString("N"); await using var factory = Factory(database);
        using var scope = factory.Services.CreateScope(); var context = scope.ServiceProvider.GetRequiredService<IMongoDbContext>();
        try
        {
            await Seed(context); using var client = Client(factory);
            var form = Form(await client.GetStringAsync("/Reservations/Create"));
            form["Email"] = "acceptance-" + Guid.NewGuid().ToString("N") + "@example.test";
            await Verify(client, context, scope.ServiceProvider, form);
            var record = await context.Database.GetCollection<ReservationRecord>("reservations").Find(FilterDefinition<ReservationRecord>.Empty).SingleAsync();
            var receipt = await context.Database.GetCollection<OutboxRecord>("outbox").Find(x => x.Id == record.Id + ":submitted").SingleAsync();
            Assert.True(await scope.ServiceProvider.GetRequiredService<HoleInHub.Business.Accounts.NotificationService>().IsEligibleAsync(
                new(receipt.Id, receipt.Recipient, receipt.Subject, receipt.Body, receipt.ReservationId, receipt.RequiredStatus, null), default));
            var sender = new SmtpEmailSender(Options.Create(new SmtpOptions
            { Host = "127.0.0.1", Port = 1025, UseStartTls = false, From = "acceptance@holeinhub.test" }),
                scope.ServiceProvider.GetRequiredService<IHostEnvironment>(), scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>());
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await sender.SendAsync(new(receipt.Id, receipt.Recipient, receipt.Subject, receipt.Body, receipt.ReservationId, receipt.RequiredStatus, null), timeout.Token);
        }
        finally { await context.Client.DropDatabaseAsync(database); }
    }

    [Theory]
    [InlineData("approve", "Confirmed")]
    [InlineData("reject", "Rejected")]
    public async Task GuestSubmissionStaffDecisionAndCancellationKeepStatusCapacityAndNotificationsConsistent(string decision, string expected)
    {
        var database = "hih_process_acceptance_" + Guid.NewGuid().ToString("N");
        using var certificate = StaffPinAccessTests.DesktopCertificate(TestNow);
        await using var baseFactory = Factory(database);
        await using var factory = baseFactory.WithWebHostBuilder(builder => builder
            .ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["StaffPin:Enabled"] = "true",
                ["StaffPin:Host"] = "localhost",
                ["StaffPin:Pepper"] = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),
                ["StaffPin:DesktopFingerprints:0"] = certificate.GetCertHashString(HashAlgorithmName.SHA256)
            }))
            .ConfigureServices(services => services.AddSingleton<IStartupFilter>(new StaffPinAccessTests.DesktopTlsFixture(certificate))));
        using var scope = factory.Services.CreateScope(); var context = scope.ServiceProvider.GetRequiredService<IMongoDbContext>();
        try
        {
            await Seed(context); using var guest = Client(factory);
            var submitted = await Verify(guest, context, scope.ServiceProvider, Form(await guest.GetStringAsync("/Reservations/Create")));
            var link = submitted.Headers.Location!.ToString();
            var record = await context.Database.GetCollection<ReservationRecord>("reservations").Find(FilterDefinition<ReservationRecord>.Empty).SingleAsync();
            var service = scope.ServiceProvider.GetRequiredService<ReservationService>();
            Assert.Equal("Pending", record.Status);
            Assert.False((await service.GetAvailabilityAsync(DateOnly.FromDateTime(record.StartUtc.AddHours(8)), TimeOnly.FromDateTime(record.StartUtc.AddHours(8)), 60, "Regular", default)).Single(x => x.Resource.Id == "bay-2").Available);
            Assert.Equal(HttpStatusCode.Redirect, (await guest.GetAsync("/BookingReview/Details/" + record.Id)).StatusCode);
            Assert.Equal(HttpStatusCode.Redirect, (await guest.GetAsync("/BookingReview?reference=" + record.Reference)).StatusCode);
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AccountUser>>();
            var staff = new AccountUser { Email = "acceptance-staff@example.test", EmailConfirmed = true, Roles = ["Staff"] };
            Assert.True((await users.CreateAsync(staff, "TestingPassword123!")).Succeeded);
            staff.Roles = ["Staff"]; Assert.True((await users.UpdateAsync(staff)).Succeeded);
            using var staffClient = Client(factory);
            await SignInStaffAsync(staffClient, staff.Email, scope.ServiceProvider);
            Assert.Equal(HttpStatusCode.Redirect, (await guest.GetAsync("/staff/review-queue")).StatusCode);
            var queuePage = await staffClient.GetStringAsync("/staff/review-queue/pending");
            Assert.Contains(record.Reference, queuePage);
            Assert.DoesNotContain("Bay schedule", queuePage);
            Assert.Contains("Pending reviews", queuePage);
            var overdueQueue = await staffClient.GetStringAsync("/staff/review-queue/overdue");
            Assert.DoesNotContain(record.Reference, overdueQueue);
            Assert.Contains("Overdue requests", overdueQueue);
            var day = TimeZoneInfo.ConvertTimeFromUtc(record.StartUtc, ReservationRules.Manila).ToString("yyyy-MM-dd");
            var workspace = await staffClient.GetStringAsync($"/BookingReview?date={day}&reference={record.Reference.ToLowerInvariant()}");
            Assert.Contains("Bay schedule", workspace);
            Assert.Contains($"data-reference=\"{record.Reference}\"", workspace);
            var guestSearch = await staffClient.GetStringAsync("/staff?reference=" + Uri.EscapeDataString($"{record.FirstName} {record.LastName}"));
            Assert.Contains($"data-reference=\"{record.Reference}\"", guestSearch);
            Assert.DoesNotContain(record.Email, workspace);
            var review = await staffClient.GetStringAsync("/BookingReview/Details/" + record.Id);
            Assert.Equal(HttpStatusCode.Redirect, (await guest.GetAsync($"/staff/reservations/{record.Id}/actions")).StatusCode);
            var quickActions = await staffClient.GetStringAsync($"/staff/reservations/{record.Id}/actions");
            Assert.DoesNotContain("<html", quickActions);
            Assert.Contains("name=\"expectedRevision\"", quickActions);
            Assert.Contains("Confirm approve booking", quickActions);
            Assert.Contains("Confirm reject booking", quickActions);
            Assert.Equal("Pending", (await service.GetStaffAsync(record.Id, default))!.Status);
            var decisionFields = new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = Csrf(review),
                ["expectedRevision"] = Regex.Match(review, "name=\"expectedRevision\"[^>]*value=\"([^\"]+)\"").Groups[1].Value,
                ["decision"] = decision,
                ["reason"] = decision == "reject" ? "Venue cannot accommodate this request." : "",
                ["fromQueue"] = "true"
            };
            if (decision == "approve")
            {
                decisionFields["participants[0].ResourceId"] = record.ResourceIds[0];
                decisionFields["participants[0].Players"] = "1";
                decisionFields["participants[0].NonPlayers"] = (record.Attendees - 1).ToString();
            }
            var decided = await staffClient.PostAsync("/BookingReview/Decide/" + record.Id, new FormUrlEncodedContent(decisionFields));
            Assert.True(decided.StatusCode == HttpStatusCode.Redirect, (await decided.Content.ReadAsStringAsync())[..Math.Min(1200, (await decided.Content.ReadAsStringAsync()).Length)]);
            var destination = decided.Headers.Location!.ToString();
            Assert.Contains("fromQueue=True", destination, StringComparison.OrdinalIgnoreCase);
            var feedback = await staffClient.GetStringAsync(destination);
            Assert.Contains("data-toast-seed data-kind=\"success\"", feedback);
            Assert.Contains(decision == "approve" ? ">Booking approved<" : ">Booking rejected<", feedback);
            Assert.DoesNotContain("data-toast-seed", await staffClient.GetStringAsync(destination));
            var stale = await staffClient.PostAsync("/BookingReview/Decide/" + record.Id, new FormUrlEncodedContent(decisionFields));
            Assert.Equal(HttpStatusCode.Redirect, stale.StatusCode);
            Assert.Contains("data-toast-seed data-kind=\"warning\"", await staffClient.GetStringAsync(stale.Headers.Location!.ToString()));
            var updated = (await service.GetStaffAsync(record.Id, default))!;
            Assert.Equal(expected, updated.Status);
            Assert.Contains(expected, await guest.GetStringAsync(link));
            Assert.Contains(updated.History, x => x.Status == expected && x.Actor == staff.Id);
            Assert.Equal(1, await context.Database.GetCollection<OutboxRecord>("outbox").CountDocumentsAsync(x => x.ReservationId == record.Id && x.Subject == expected));
            if (decision == "approve")
            {
                var page = await guest.GetStringAsync(link.Replace("/Confirmation/", "/Status/", StringComparison.Ordinal));
                var token = WebUtility.UrlDecode(link.Split("token=")[1]);
                var cancellation = await guest.PostAsync("/Reservations/RequestCancellation/" + record.Id, new FormUrlEncodedContent(new Dictionary<string, string>
                { ["__RequestVerificationToken"] = Csrf(page), ["token"] = token }));
                Assert.Equal(HttpStatusCode.Redirect, cancellation.StatusCode);
                updated = (await service.GetStaffAsync(record.Id, default))!;
                Assert.False(updated.CancellationRequested);
                Assert.Equal("Cancelled", updated.Status);
                Assert.Contains("Cancelled", await guest.GetStringAsync(link));
            }
            Assert.True((await service.GetAvailabilityAsync(DateOnly.FromDateTime(record.StartUtc.AddHours(8)), TimeOnly.FromDateTime(record.StartUtc.AddHours(8)), 60, "Regular", default)).Single(x => x.Resource.Id == "bay-2").Available);
        }
        finally { await context.Client.DropDatabaseAsync(database); }
    }

    [Fact]
    public async Task VerifiedCustomerSkipsCodeAndCanOnlySeeOwnBookings()
    {
        var database = "hih_staff_flow_" + Guid.NewGuid().ToString("N"); await using var factory = Factory(database);
        using var scope = factory.Services.CreateScope(); var context = scope.ServiceProvider.GetRequiredService<IMongoDbContext>();
        try
        {
            await Seed(context);
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AccountUser>>();
            var customer = new AccountUser { Email = "customer@example.test", EmailConfirmed = true };
            Assert.True((await users.CreateAsync(customer, "TestingPassword123!")).Succeeded);
            using var client = Client(factory);
            var login = await client.GetStringAsync("/account/sign-in");
            var signedIn = await client.PostAsync("/account/sign-in", new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = Csrf(login), ["Email"] = customer.Email, ["Password"] = "TestingPassword123!" }));
            Assert.Equal(HttpStatusCode.Redirect, signedIn.StatusCode);
            var bookingPage = await client.GetStringAsync("/Reservations/Create"); Assert.DoesNotContain("Send verification code", bookingPage);
            var form = Form(bookingPage); form["Email"] = "attacker@example.test";
            var submitted = await client.PostAsync("/Reservations/Create", new FormUrlEncodedContent(form)); Assert.Equal(HttpStatusCode.Redirect, submitted.StatusCode);
            var saved = await context.Database.GetCollection<ReservationRecord>("reservations").Find(FilterDefinition<ReservationRecord>.Empty).SingleAsync();
            Assert.Equal(customer.Id, saved.AccountId); Assert.Null(saved.EmailVerificationId);
            Assert.Equal(customer.Email, saved.Email); Assert.Equal("", (await users.FindByIdAsync(customer.Id))!.FirstName);
            var prefilled = await client.GetStringAsync("/Reservations/Create");
            Assert.Contains("value=\"Guest\"", prefilled); Assert.Contains("value=\"Player\"", prefilled); Assert.Contains("value=\"09123456789\"", prefilled);
            var mine = await client.GetStringAsync("/Reservations"); Assert.Contains(saved.Reference, mine); Assert.Contains("Upcoming", mine);
            await context.Database.GetCollection<ReservationRecord>("reservations").InsertOneAsync(new ReservationRecord { AccountId = "another-owner", Email = "another@example.test", Reference = "PRIVATE-OTHER", Service = "Regular", ResourceIds = ["bay-3"], Attendees = 1, CreatedUtc = TestNow.UtcDateTime, ReviewDeadlineUtc = TestNow.UtcDateTime.AddMinutes(30), IdempotencyKey = "private", SubmissionHash = "private", StartUtc = TestNow.UtcDateTime.AddDays(2), EndUtc = TestNow.UtcDateTime.AddDays(2).AddHours(1) });
            Assert.DoesNotContain("PRIVATE-OTHER", await client.GetStringAsync("/Reservations"));
            Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/staff/review/Details/" + saved.Id)).StatusCode);
        }
        finally { await context.Client.DropDatabaseAsync(database); }
    }
    [Fact]
    public async Task AvailabilityRejectsPastStartAndShortReviewLead()
    {
        var database = "hih_past_times_" + Guid.NewGuid().ToString("N"); await using var factory = Factory(database);
        using var scope = factory.Services.CreateScope(); var context = scope.ServiceProvider.GetRequiredService<IMongoDbContext>();
        try
        {
            await Seed(context); using var client = Client(factory); var date = TestNow.ToString("yyyy-MM-dd");
            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/Reservations/CheckAvailability?date={date}&startTime=11:00&durationMinutes=60&service=Regular")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/Reservations/CheckAvailability?date={date}&startTime=12:30&durationMinutes=60&service=VIP")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/Reservations/CheckAvailability?date={date}&startTime=12:30&durationMinutes=60&service=Regular")).StatusCode);
        }
        finally { await context.Client.DropDatabaseAsync(database); }
    }
    [Fact]
    public async Task ConflictDuringVerificationKeepsDraftEditable()
    {
        var database = "hih_conflict_draft_" + Guid.NewGuid().ToString("N"); await using var factory = Factory(database);
        using var scope = factory.Services.CreateScope(); var context = scope.ServiceProvider.GetRequiredService<IMongoDbContext>();
        try
        {
            await Seed(context); using var client = Client(factory); var form = Form(await client.GetStringAsync("/Reservations/Create"));
            Assert.Equal(HttpStatusCode.Redirect, (await client.PostAsync("/Reservations/Create", new FormUrlEncodedContent(form))).StatusCode);
            var page = await client.GetStringAsync("/Reservations/Verify");
            var email = await context.Database.GetCollection<OutboxRecord>("outbox").Find(x => x.Subject == "Your booking verification code").SingleAsync();
            var plain = scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>().CreateProtector("BookingEmail").Unprotect(email.Body[10..]); var code = Regex.Match(plain, "[0-9]{6}").Value;
            var (start, end) = ReservationRules.Interval(DateOnly.Parse(form["Date"]), new TimeOnly(14, 0), 60);
            await scope.ServiceProvider.GetRequiredService<ReservationPersistence>().InsertAsync(new ReservationRecord { AccountId = "another", Email = "another@example.test", Reference = "CONFLICT-OTHER", Attendees = 1, CreatedUtc = TestNow.UtcDateTime, IdempotencyKey = "other", SubmissionHash = "other", Service = "Regular", ResourceIds = ["bay-2"], StartUtc = start, EndUtc = end, ReviewDeadlineUtc = TestNow.UtcDateTime.AddHours(1) }, default);
            var conflict = await client.PostAsync("/Reservations/Verify", new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = Csrf(page), ["code"] = code }));
            Assert.Equal(HttpStatusCode.OK, conflict.StatusCode); Assert.Contains("Back to booking", await conflict.Content.ReadAsStringAsync());
            var edited = await client.GetStringAsync("/Reservations/Create?editDraft=true"); Assert.Contains("value=\"Guest\"", edited); Assert.Contains("value=\"09123456789\"", edited);
            form["__RequestVerificationToken"] = Csrf(edited); form["ResourceIds"] = "bay-3";
            var submitted = await client.PostAsync("/Reservations/Create", new FormUrlEncodedContent(form)); Assert.Equal(HttpStatusCode.Redirect, submitted.StatusCode);
            Assert.Contains("/Reservations/Confirmation/", submitted.Headers.Location!.ToString());
        }
        finally { await context.Client.DropDatabaseAsync(database); }
    }
    [Fact]
    public async Task DatabaseOutagePreservesBookingDetailsAndOriginalSubmissionKey()
    {
        var database = "hih_recovery_" + Guid.NewGuid().ToString("N");
        await using var factory = Factory(database).WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            var worker = services.FirstOrDefault(x => x.ImplementationType == typeof(NotificationWorker));
            if (worker != null) { services.Remove(worker); }
            services.AddSingleton<SwitchableContext>(provider => new SwitchableContext(new MongoDbContext(provider.GetRequiredService<IOptions<MongoDbOptions>>())));
            services.AddSingleton<IMongoDbContext>(provider => provider.GetRequiredService<SwitchableContext>());
        }));
        using var scope = factory.Services.CreateScope(); var context = scope.ServiceProvider.GetRequiredService<SwitchableContext>();
        try
        {
            await Seed(context); using var client = Client(factory); var form = Form(await client.GetStringAsync("/Reservations/Create")); context.Offline = true;
            var failed = await client.PostAsync("/Reservations/Create", new FormUrlEncodedContent(form));
            Assert.Equal(HttpStatusCode.ServiceUnavailable, failed.StatusCode); var body = await failed.Content.ReadAsStringAsync(); Assert.Contains("Try again", body); Assert.DoesNotContain("sensitive database details", body);
            context.Offline = false; context.FailOnRead = context.Reads + 2;
            Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/Reservations/Create?recover=true")).StatusCode);
            context.FailOnRead = null; var restored = await client.GetStringAsync("/Reservations/Create?recover=true");
            Assert.Contains("value=\"Guest\"", restored); Assert.Contains("value=\"09123456789\"", restored); Assert.Contains(form["IdempotencyKey"], restored); Assert.Contains("restored", restored);
        }
        finally { context.Offline = false; await context.Client.DropDatabaseAsync(database); }
    }
    [Fact]
    public async Task SimulatedCheckoutIsUnavailableOutsideDevelopmentOrDemo()
    {
        await using var factory = Factory("unused_checkout_test", "Testing"); using var client = Client(factory);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/Reservations/Checkout/any")).StatusCode);
    }
}
