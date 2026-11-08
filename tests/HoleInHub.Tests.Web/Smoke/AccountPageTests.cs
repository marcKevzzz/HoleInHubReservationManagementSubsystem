using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace HoleInHub.Tests.Web.Smoke;

public sealed class AccountPageTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;
    [Theory]
    [InlineData("FirstName", "Guest2", "Use letters for names")]
    [InlineData("LastName", "Player٣", "Use letters for names")]
    [InlineData("Phone", "+639123456789", "digits only")]
    [InlineData("Phone", "0912abc6789", "digits only")]
    public async Task SignUpRejectsForgedNamesAndNonNumericPhones(string field, string value, string message)
    {
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.UseEnvironment("Testing").UseSetting("Authentication:Google:Enabled", "false"));
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var html = await client.GetStringAsync("/account/sign-up");
        var form = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = WebUtility.HtmlDecode(System.Text.RegularExpressions.Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value),
            ["FirstName"] = "Guest",
            ["LastName"] = "Player",
            ["Phone"] = "09123456789",
            ["Email"] = "validation@example.test",
            ["Password"] = "ValidPassword123!",
            ["ConfirmPassword"] = "ValidPassword123!"
        };
        form[field] = value;
        var response = await client.PostAsync("/account/sign-up", new FormUrlEncodedContent(form));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(message, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task SignUpCollectsBookingContactAndGoogleIsOptional()
    {
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.UseEnvironment("Testing").UseSetting("Authentication:Google:Enabled", "false")); using var client = factory.CreateClient();
        var html = await client.GetStringAsync("/account/sign-up");
        Assert.Contains("autocomplete=\"given-name\"", html);
        Assert.Contains("autocomplete=\"family-name\"", html);
        Assert.Contains("autocomplete=\"tel\"", html);
        Assert.DoesNotContain("Continue with Google", html);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync("/account/google", new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = WebUtility.HtmlDecode(System.Text.RegularExpressions.Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value) }))).StatusCode);
    }
    public AccountPageTests(WebApplicationFactory<Program> factory) => _client = factory.CreateClient();

    [Theory]
    [InlineData("/account/sign-up")]
    [InlineData("/account/sign-in")]
    [InlineData("/account/forgot-password")]
    [InlineData("/account/verify-email")]
    [InlineData("/account/reset-password")]
    public async Task AccountPagesHaveProtectedForms(string path)
    {
        var response = await _client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        if (path != "/account/verify-email") { Assert.Contains("__RequestVerificationToken", html); }
        else { Assert.DoesNotContain("name=\"Email\"", html); Assert.Contains("Sign in to verify", html); }
        Assert.Matches(@"class=""(?:[^""]*\s)?auth-shell(?:\s[^""]*)?""", html);
        Assert.DoesNotContain("site-header", html);
        Assert.DoesNotContain("site-footer", html);
        Assert.Contains(path == "/account/forgot-password" ? "Back to sign in" : "Back to home", html);
        if (path != "/account/verify-email") { Assert.Contains("placeholder=\" \"", html); }
        Assert.Contains("/js/ui-motion.js", html);
        Assert.True(html.IndexOf("/js/vendor/gsap.min.js", StringComparison.Ordinal) < html.IndexOf("/js/ui-motion.js", StringComparison.Ordinal));
        Assert.True(html.IndexOf("/js/ui-motion.js", StringComparison.Ordinal) < html.IndexOf("/js/account.js", StringComparison.Ordinal));
        Assert.Contains("/js/button-motion.js", html);
    }

    [Fact]
    public async Task SignUpRejectsMissingAntiforgeryToken()
    {
        var response = await _client.PostAsync("/account/sign-up", new FormUrlEncodedContent(new Dictionary<string, string>()));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("sign-up")]
    [InlineData("forgot-password")]
    [InlineData("reset-password")]
    [InlineData("verify-email")]
    public async Task AccountPagesCarryLocalBookingReturnUrl(string page)
    {
        using var isolatedFactory = new WebApplicationFactory<Program>();
        using var client = isolatedFactory.CreateClient();
        var html = WebUtility.HtmlDecode(await client.GetStringAsync($"/account/{page}?returnUrl=%2FReservations%2FCreate"));
        Assert.Contains("name=\"ReturnUrl\"", html);
        Assert.Contains("value=\"/Reservations/Create\"", html);
    }
    [Theory]
    [InlineData("https://example.test/steal")]
    [InlineData("//example.test/steal")]
    public async Task AccountPagesDiscardExternalReturnUrls(string destination)
    {
        using var isolatedFactory = new WebApplicationFactory<Program>();
        using var client = isolatedFactory.CreateClient();
        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/account/sign-up?returnUrl=" + Uri.EscapeDataString(destination)));
        Assert.DoesNotContain(destination, html);
    }
}
