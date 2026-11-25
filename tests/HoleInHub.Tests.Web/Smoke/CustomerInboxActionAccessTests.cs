using System.Net;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HoleInHub.Tests.Web.Smoke;

public sealed partial class StaffBlockHttpTests
{
    [Theory]
    [InlineData("read")]
    [InlineData("read-all")]
    [InlineData("delete")]
    public async Task CustomerInboxWritesRequireAuthenticationAndAntiforgery(string operation)
    {
        await using var factory = Factory("test_customer_inbox_access").WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.Configure<CookieAuthenticationOptions>(
                IdentityConstants.ApplicationScheme, options => options.ForwardDefault = "BlockTest")));
        using var anonymous = Client(factory, "");
        using var customer = Client(factory, "Customer");
        var payload = new Dictionary<string, string> { ["operation"] = operation, ["id"] = "booking:0", ["activityTicks"] = "1" };
        using var unauthorized = await anonymous.PostAsync("/notifications/action", new FormUrlEncodedContent(payload));
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        using var missingToken = await customer.PostAsync("/notifications/action", new FormUrlEncodedContent(payload));
        Assert.Equal(HttpStatusCode.BadRequest, missingToken.StatusCode);
    }
}
