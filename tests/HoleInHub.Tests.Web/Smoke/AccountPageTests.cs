using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace HoleInHub.Tests.Web.Smoke;

public sealed class AccountPageTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;
    public AccountPageTests(WebApplicationFactory<Program> factory) => _client = factory.CreateClient();

    [Theory]
    [InlineData("/account/sign-up")]
    [InlineData("/account/sign-in")]
    [InlineData("/account/forgot-password")]
    [InlineData("/account/verify-email")]
    public async Task AccountPagesHaveProtectedForms(string path)
    {
        var response = await _client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("__RequestVerificationToken", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task SignUpRejectsMissingAntiforgeryToken()
    {
        var response = await _client.PostAsync("/account/sign-up", new FormUrlEncodedContent(new Dictionary<string, string>()));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
