using System.Net;
using System.Text.RegularExpressions;
using HoleInHub.Data.Context;
using HoleInHub.Data.Migrations.Definitions;
using HoleInHub.Data.Reservations;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using Xunit;

namespace HoleInHub.Tests.Web.Smoke;

[Trait("Category", "Integration")]
public sealed class GuestRecoveryTests
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private static WebApplicationFactory<Program> Factory(string database, Clock clock) => new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder
        .UseEnvironment("Testing").UseSetting("Authentication:Google:Enabled", "false").ConfigureLogging(x => x.ClearProviders())
        .ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["MongoDb:ConnectionString"] = "mongodb://localhost:27017/?replicaSet=rs0",
            ["MongoDb:ReplicaSetName"] = "rs0",
            ["MongoDb:DatabaseName"] = database,
            ["Smtp:Host"] = ""
        })).ConfigureServices(x => x.AddSingleton<TimeProvider>(clock)));
    private static HttpClient Client(WebApplicationFactory<Program> factory) => factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
    private static string Csrf(string html) => WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
    private static async Task Seed(IMongoDbContext context)
    {
        await new Migration_002_Reservations().UpAsync(context.Database, default); await new AccountsMigration().UpAsync(context.Database, default);
        await new Migration_004_BookingProcess().UpAsync(context.Database, default); await new Migration_005_GoogleAccounts().UpAsync(context.Database, default);
        await new Migration_006_SchemaHardening().UpAsync(context.Database, default); await new Migration_007_GuestReservationAccess().UpAsync(context.Database, default);
    }
    private static ReservationRecord Booking(string id, string email, string owner = "guest:old") => new()
    {
        Id = id,
        AccountId = owner,
        Email = email,
        EmailVerificationId = "durable-original-proof",
        Service = "Regular",
        Status = "Confirmed",
        Reference = "HIH-" + id,
        IdempotencyKey = id,
        SubmissionHash = "hash",
        ResourceIds = ["bay-2"],
        Attendees = 2,
        StartUtc = DateTime.UtcNow.AddDays(2),
        EndUtc = DateTime.UtcNow.AddDays(2).AddHours(1),
        CreatedUtc = DateTime.UtcNow,
        ReviewDeadlineUtc = DateTime.UtcNow.AddHours(1),
        AccessTokenHash = HoleInHub.Business.Accounts.EmailVerificationService.Hash("original-token".PadRight(64, 'A'))
    };
    private static async Task<string> Begin(HttpClient client, string email, string returnUrl = "/guest/reservations")
    {
        var page = await client.GetStringAsync("/guest/reservations/find");
        var response = await client.PostAsync("/guest/reservations/find", new FormUrlEncodedContent(new Dictionary<string, string>
        { ["__RequestVerificationToken"] = Csrf(page), ["Email"] = email, ["ReturnUrl"] = returnUrl }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode); Assert.Equal("/guest/reservations/verify", response.Headers.Location!.ToString());
        return await client.GetStringAsync(response.Headers.Location);
    }
    private static Task<HttpResponseMessage> Verify(HttpClient client, string page, string code) => client.PostAsync("/guest/reservations/verify", new FormUrlEncodedContent(new Dictionary<string, string>
    { ["__RequestVerificationToken"] = Csrf(page), ["code"] = code, ["Email"] = "attacker@example.test", ["ReturnUrl"] = "https://evil.example" }));
    private static async Task<string> Code(IMongoDbContext context, IServiceProvider services, string email)
    {
        var mail = await context.Database.GetCollection<OutboxRecord>("outbox").Find(x => x.Recipient == email && x.Subject == "Access your Hole In Hub reservations").SortByDescending(x => x.DueUtc).FirstAsync();
        return Regex.Match(services.GetRequiredService<IDataProtectionProvider>().CreateProtector("BookingEmail").Unprotect(mail.Body[10..]), "[0-9]{6}").Value;
    }

    [Fact]
    public async Task VerifiedGuestCanReopenConfirmedBookingsAndCancelWithoutAnAccountOrJavascript()
    {
        var database = "hih_guest_access_" + Guid.NewGuid().ToString("N"); var clock = new Clock(); await using var factory = Factory(database, clock);
        using var scope = factory.Services.CreateScope(); var context = scope.ServiceProvider.GetRequiredService<IMongoDbContext>();
        try
        {
            await Seed(context);
            await context.Database.GetCollection<ReservationRecord>("reservations").InsertManyAsync([Booking("own", "guest@example.test"), Booking("account", "guest@example.test", "account-owner"), Booking("other", "other@example.test")]);
            using var client = Client(factory);
            Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/guest/reservations/details/own")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/guest/reservations/find", new FormUrlEncodedContent(new Dictionary<string, string> { ["Email"] = "guest@example.test" }))).StatusCode);
            var page = await Begin(client, "guest@example.test", "https://evil.example");
            Assert.DoesNotContain("name=\"Email\"", page); Assert.Contains("verification-shell", page); Assert.DoesNotContain("site-header", page);
            var code = await Code(context, scope.ServiceProvider, "guest@example.test");
            var wrong = await Verify(client, page, "wrong"); Assert.Equal(HttpStatusCode.OK, wrong.StatusCode);
            var result = await Verify(client, await wrong.Content.ReadAsStringAsync(), code);
            Assert.Equal("/guest/reservations", result.Headers.Location!.ToString());
            Assert.Contains(result.Headers.GetValues("Set-Cookie"), x => x.StartsWith("hih-guest-access=", StringComparison.Ordinal) && x.Contains("httponly") && x.Contains("secure") && x.Contains("samesite=lax"));
            var history = await client.GetStringAsync(result.Headers.Location); Assert.Contains("HIH-own", history); Assert.Contains("Confirmed", history);
            Assert.DoesNotContain("HIH-account", history); Assert.DoesNotContain("HIH-other", history); Assert.DoesNotContain("token=", history);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/guest/reservations/details/account")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/Reservations/Status/other")).StatusCode);
            Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/my-profile")).StatusCode);
            var detail = await client.GetStringAsync("/guest/reservations/details/own"); Assert.Contains("Back to your reservations", detail);
            Assert.Contains("href=\"/guest/reservations/details/own\"", detail);
            var cancelUrl = WebUtility.HtmlDecode(Regex.Match(detail, "action=\"(/Reservations/RequestCancellation/[^\"]+)\"").Groups[1].Value);
            Assert.Equal("/Reservations/RequestCancellation/own?guest=true", cancelUrl);
            var cancel = await client.PostAsync(cancelUrl, new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = Csrf(detail) }));
            Assert.Equal(HttpStatusCode.Redirect, cancel.StatusCode);
            var cancelled = await context.Database.GetCollection<ReservationRecord>("reservations").Find(x => x.Id == "own").SingleAsync();
            Assert.Equal("Cancelled", cancelled.Status);
            Assert.False(cancelled.CancellationRequested);
            var leave = await client.PostAsync("/guest/reservations/leave", new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = Csrf(history) }));
            Assert.Equal(HttpStatusCode.Redirect, leave.StatusCode); Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/guest/reservations")).StatusCode);
            using var stranger = Client(factory);
            Assert.Equal(HttpStatusCode.OK, (await stranger.GetAsync("/Reservations/Status/own?token=" + "original-token".PadRight(64, 'A'))).StatusCode);
            Assert.Equal(HttpStatusCode.Redirect, (await stranger.GetAsync("/guest/reservations")).StatusCode);
        }
        finally { await context.Client.DropDatabaseAsync(database); }
    }

    [Fact]
    public async Task ExpiredChallengeCanResendWithoutEmailInputAndKeepsItsBoundDestination()
    {
        var database = "hih_guest_resend_" + Guid.NewGuid().ToString("N"); var clock = new Clock(); await using var factory = Factory(database, clock);
        using var scope = factory.Services.CreateScope(); var context = scope.ServiceProvider.GetRequiredService<IMongoDbContext>();
        try
        {
            await Seed(context); using var client = Client(factory);
            var page = await Begin(client, "resend@example.test", "/guest/reservations/details/booking");
            var original = await Code(context, scope.ServiceProvider, "resend@example.test");
            clock.Now = clock.Now.AddMinutes(10);
            var expired = await Verify(client, page, original); Assert.Equal(HttpStatusCode.OK, expired.StatusCode);
            var expiredPage = await expired.Content.ReadAsStringAsync(); Assert.Contains("incorrect or expired", expiredPage);
            var resend = await client.PostAsync("/guest/reservations/resend", new FormUrlEncodedContent(new Dictionary<string, string>
            { ["__RequestVerificationToken"] = Csrf(expiredPage), ["Email"] = "attacker@example.test" }));
            var fresh = await resend.Content.ReadAsStringAsync(); Assert.Contains("resend@example.test", fresh); Assert.DoesNotContain("name=\"Email\"", fresh);
            var result = await Verify(client, fresh, await Code(context, scope.ServiceProvider, "resend@example.test"));
            Assert.Equal("/guest/reservations/details/booking", result.Headers.Location!.ToString());
            Assert.Equal(2, await context.Database.GetCollection<OutboxRecord>("outbox").CountDocumentsAsync(x => x.Recipient == "resend@example.test"));
        }
        finally { await context.Client.DropDatabaseAsync(database); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RecoveryEndpointsEnforceIpLimits(bool verification)
    {
        var database = "hih_guest_limit_" + Guid.NewGuid().ToString("N"); var clock = new Clock(); await using var factory = Factory(database, clock);
        using var scope = factory.Services.CreateScope(); var context = scope.ServiceProvider.GetRequiredService<IMongoDbContext>();
        try
        {
            await Seed(context); using var client = Client(factory);
            var page = await Begin(client, "limit@example.test");
            if (verification)
            {
                for (var i = 0; i < 30; i++) { Assert.Equal(HttpStatusCode.OK, (await Verify(client, page, "000000")).StatusCode); }
                var limited = await Verify(client, page, "000000");
                Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
                Assert.Contains("Please wait", await limited.Content.ReadAsStringAsync());
            }
            else
            {
                for (var i = 0; i < 8; i++) { page = await Begin(client, "limit" + i + "@example.test"); }
                clock.Now = clock.Now.AddMinutes(1);
                var resend = await client.PostAsync("/guest/reservations/resend", new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = Csrf(page) }));
                Assert.Equal(HttpStatusCode.OK, resend.StatusCode);
                var find = await client.GetStringAsync("/guest/reservations/find");
                var limited = await client.PostAsync("/guest/reservations/find", new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = Csrf(find), ["Email"] = "another@example.test" }));
                Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
                Assert.Contains("Please wait", await limited.Content.ReadAsStringAsync());
            }
        }
        finally { await context.Client.DropDatabaseAsync(database); }
    }

    [Fact]
    public async Task UnknownEmailIsVerifiedBeforeEmptyHistoryAndExpiredAccessCannotBeReused()
    {
        var database = "hih_guest_empty_" + Guid.NewGuid().ToString("N"); var clock = new Clock(); await using var factory = Factory(database, clock);
        using var scope = factory.Services.CreateScope(); var context = scope.ServiceProvider.GetRequiredService<IMongoDbContext>();
        try
        {
            await Seed(context); using var client = Client(factory);
            var page = await Begin(client, "empty@example.test"); Assert.DoesNotContain("No guest reservations", page);
            var code = await Code(context, scope.ServiceProvider, "empty@example.test");
            var accepted = await Verify(client, page, code); Assert.Equal(HttpStatusCode.Redirect, accepted.StatusCode);
            var history = await client.GetStringAsync("/guest/reservations"); Assert.Contains("No guest reservations found for this email.", history);
            clock.Now = clock.Now.AddMinutes(30);
            Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/guest/reservations")).StatusCode);
            var expiredDetail = await client.GetAsync("/guest/reservations/details/missing"); Assert.Contains("returnUrl=", expiredDetail.Headers.Location!.ToString());
            using var tampered = Client(factory); tampered.DefaultRequestHeaders.Add("Cookie", "hih-guest-access=tampered");
            Assert.Equal(HttpStatusCode.Redirect, (await tampered.GetAsync("/guest/reservations")).StatusCode);
            // A browser removes expired cookies; action URLs must still retain their recovery destination.
            using var expiredBrowser = Client(factory);
            var find = await expiredBrowser.GetStringAsync("/guest/reservations/find");
            var expiredAction = await expiredBrowser.PostAsync("/Reservations/RequestCancellation/missing?guest=true", new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = Csrf(find) }));
            Assert.Equal(HttpStatusCode.Redirect, expiredAction.StatusCode);
            Assert.Contains("guest/reservations/find", expiredAction.Headers.Location!.ToString());
            Assert.Contains("returnUrl=", expiredAction.Headers.Location.ToString());
        }
        finally { await context.Client.DropDatabaseAsync(database); }
    }
}
