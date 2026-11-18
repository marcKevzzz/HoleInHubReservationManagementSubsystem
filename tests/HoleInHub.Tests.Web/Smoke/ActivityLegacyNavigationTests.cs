using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
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
using Xunit;

namespace HoleInHub.Tests.Web.Smoke;

public sealed class ActivityLegacyNavigationTests
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
    [InlineData("", "/admin/activity", HttpStatusCode.Unauthorized, null)]
    [InlineData("Customer", "/admin/activity", HttpStatusCode.Forbidden, null)]
    [InlineData("Staff", "/admin/activity", HttpStatusCode.Forbidden, null)]
    [InlineData("Manager", "/admin/activity", HttpStatusCode.Forbidden, null)]
    [InlineData("Admin", "/admin/activity", HttpStatusCode.Redirect, "/staff/activity?category=access")]
    [InlineData("", "/manager/activity", HttpStatusCode.Unauthorized, null)]
    [InlineData("Customer", "/manager/activity", HttpStatusCode.Forbidden, null)]
    [InlineData("Staff", "/manager/activity", HttpStatusCode.Forbidden, null)]
    [InlineData("Manager", "/manager/activity", HttpStatusCode.Redirect, "/staff/activity")]
    [InlineData("Admin", "/manager/activity", HttpStatusCode.Redirect, "/staff/activity")]
    public async Task LegacyActivityKeepsItsAuthorizationBeforeRedirecting(string role, string path, HttpStatusCode expected, string? destination)
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder
            .UseEnvironment("Testing").ConfigureLogging(logging => logging.ClearProviders())
            .ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MongoDb:ConnectionString"] = "mongodb://localhost:27017/?replicaSet=rs0",
                ["MongoDb:DatabaseName"] = "test_activity_redirects",
                ["MongoDb:ReplicaSetName"] = "rs0",
                ["Smtp:Host"] = ""
            }))
            .ConfigureTestServices(services =>
            {
                services.AddDataProtection().UseEphemeralDataProtectionProvider();
                services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, TestAuth>("ActivityRedirectTest", _ => { });
                services.Configure<CookieAuthenticationOptions>(StaffPinAccess.Scheme, options => options.ForwardDefault = "ActivityRedirectTest");
            }));
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        if (role.Length > 0) { client.DefaultRequestHeaders.Add("X-Test-Role", role); }
        using var response = await client.GetAsync(path);
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal(destination, response.Headers.Location?.OriginalString);
    }
}
