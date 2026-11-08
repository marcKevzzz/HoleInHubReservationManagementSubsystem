using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
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

[Trait("Category", "Integration")]
public sealed class CustomersAccessTests
{
    private sealed class TestAuth(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var role = Request.Headers["X-Test-Role"].ToString();
            return Task.FromResult(role.Length == 0 ? AuthenticateResult.NoResult() : AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity([
                    new Claim(ClaimTypes.NameIdentifier, "test-user"), new Claim(ClaimTypes.Role, role)
                ], Scheme.Name)), Scheme.Name)));
        }
    }
    [Theory]
    [InlineData("", HttpStatusCode.Unauthorized)]
    [InlineData("Customer", HttpStatusCode.Forbidden)]
    [InlineData("Staff", HttpStatusCode.OK)]
    [InlineData("Manager", HttpStatusCode.OK)]
    [InlineData("Admin", HttpStatusCode.OK)]
    public async Task CustomerDirectoryAndProfilesRequireAWorkspaceRole(string role, HttpStatusCode expected)
    {
        var database = "test_customers_access_" + Guid.NewGuid().ToString("N");
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder
            .UseEnvironment("Testing").ConfigureLogging(logging => logging.ClearProviders())
            .ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MongoDb:ConnectionString"] = "mongodb://localhost:27017/?replicaSet=rs0",
                ["MongoDb:DatabaseName"] = database,
                ["MongoDb:ReplicaSetName"] = "rs0",
                ["Smtp:Host"] = ""
            }))
            .ConfigureTestServices(services =>
            {
                services.AddDataProtection().UseEphemeralDataProtectionProvider();
                services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, TestAuth>("CustomersTest", _ => { });
                services.Configure<CookieAuthenticationOptions>(StaffPinAccess.Scheme, options => options.ForwardDefault = "CustomersTest");
            }));
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        if (role.Length > 0) { client.DefaultRequestHeaders.Add("X-Test-Role", role); }
        var booking = new ReservationRecord
        {
            AccountId = "customer",
            Reference = "HIH-CUSTOMER",
            FirstName = "Alex",
            LastName = "Santos",
            Email = "alex@example.test",
            Phone = "09175550123",
            Status = "Confirmed",
            Service = "Regular",
            StartUtc = DateTime.UtcNow.AddDays(1),
            EndUtc = DateTime.UtcNow.AddDays(1).AddHours(1),
            CreatedUtc = DateTime.UtcNow,
            ResourceIds = ["bay-2"],
            IdempotencyKey = "test",
            SubmissionHash = "test"
        };
        IMongoDbContext? context = null;
        try
        {
            if (expected == HttpStatusCode.OK)
            {
                context = factory.Services.GetRequiredService<IMongoDbContext>();
                await new Migration_002_Reservations().UpAsync(context.Database, default);
                await context.Database.GetCollection<ReservationRecord>("reservations").InsertOneAsync(booking);
            }
            var list = await client.GetAsync("/staff/customers?search=Alex");
            Assert.Equal(expected, list.StatusCode);
            var profile = await client.GetAsync("/staff/customers/" + booking.Id);
            Assert.Equal(expected, profile.StatusCode);
            if (expected == HttpStatusCode.OK)
            {
                Assert.Contains("Alex Santos", await list.Content.ReadAsStringAsync());
                var html = await profile.Content.ReadAsStringAsync();
                Assert.Contains("Reservation history", html);
                Assert.Contains("HIH-CUSTOMER", html);
                Assert.Contains("no-store", profile.Headers.CacheControl!.ToString());
                Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/staff/customers/missing")).StatusCode);
            }
        }
        finally { if (context is not null) { await context.Client.DropDatabaseAsync(database); } }
    }
}
