using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace HoleInHub.Tests.Web.Smoke;

public sealed class AccountMenuTests
{
    [Fact]
    public async Task GuestMenuOffersSignInAndCreateAccount()
    {
        await using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();
        var html = await client.GetStringAsync("/");
        Assert.Contains("id=\"account-menu\"", html);
        Assert.Contains("popover=\"auto\"", html);
        Assert.DoesNotContain("Sign in to manage your reservations and plan your next visit.", html);
        Assert.Matches("class=\"button button-tertiary\"[^>]*data-no-icon[^>]*href=\"/account/sign-up\"", html);
        Assert.Contains("/js/page-transitions.js", html);
        Assert.Contains("/js/ui-motion.js", html);
        Assert.True(html.IndexOf("/js/ui-motion.js", StringComparison.Ordinal) < html.IndexOf("/js/motion.js", StringComparison.Ordinal));
        Assert.True(html.IndexOf("/js/vendor/ScrollTrigger.min.js", StringComparison.Ordinal) < html.IndexOf("/js/motion.js", StringComparison.Ordinal));
        Assert.DoesNotContain("action=\"/account/sign-out\"", html);
    }

    [Fact]
    public async Task SignedInMenuOffersReservationsAndProtectedLogout()
    {
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.AddAuthentication("MenuTest")
                .AddScheme<AuthenticationSchemeOptions, MenuAuthenticationHandler>("MenuTest", _ => { })));
        using var client = factory.CreateClient();
        var html = await client.GetStringAsync("/");
        Assert.Contains("href=\"/my-reservations\"", html);
        Assert.Contains("href=\"/transactions\"", html);
        Assert.Contains("href=\"/my-profile\"", html);
        Assert.DoesNotContain("data-account-placeholder", html);
        Assert.Contains("action=\"/account/sign-out\"", html);
        Assert.Contains("__RequestVerificationToken", html);
        Assert.Contains("id=\"logout-confirmation\"", html);
        Assert.Contains("data-confirm-logout", html);
        Assert.DoesNotContain("account-menu-secondary", html);
        Assert.DoesNotContain("Staff workspace", html);
    }

    private sealed class MenuAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(
            AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, "menu-test"), new Claim(ClaimTypes.Role, "Admin")], Scheme.Name)), Scheme.Name)));
    }
}
