using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace HoleInHub.Tests.Web.Smoke;

public sealed class CustomerLanguageTests
{
    [Fact]
    public async Task BrowserLanguageFallsBackAndExplicitChoicePersists()
    {
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseEnvironment("Testing").ConfigureLogging(logging => logging.ClearProviders())
                .ConfigureTestServices(services => services.AddDataProtection().UseEphemeralDataProtectionProvider()));
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });
        const string signIn = "/account/sign-in";
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("fr-FR");
        Assert.Contains("<html lang=\"en\"", await client.GetStringAsync(signIn));
        client.DefaultRequestHeaders.AcceptLanguage.Clear();
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("ko-KR");
        var korean = WebUtility.HtmlDecode(await client.GetStringAsync(signIn));
        Assert.Contains("<html lang=\"ko\"", korean);
        Assert.Contains("다시 오신 것을 환영합니다", korean);
        var csrf = Regex.Match(korean, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        Assert.NotEmpty(csrf);
        using var switchLanguage = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = csrf,
            ["culture"] = "en",
            ["returnUrl"] = "https://example.invalid"
        });
        using var response = await client.PostAsync("/language", switchLanguage);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/", response.Headers.Location?.OriginalString);
        Assert.Contains("<html lang=\"en\"", await client.GetStringAsync(signIn));
    }
}
