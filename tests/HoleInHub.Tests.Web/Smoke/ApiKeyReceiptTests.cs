using System.Security.Claims;
using HoleInHub.Web.Accounts;
using HoleInHub.Web.ViewModels;
using HoleInHubReservationManagementSubsystem.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Xunit;

namespace HoleInHub.Tests.Web.Smoke;

public sealed class ApiKeyReceiptTests
{
    [Fact]
    public void ConfirmationShowsTheIssuingAdminTheKeyOnceAndRefreshReturnsToTheList()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var receipt = Guid.NewGuid();
        var issued = new IssuedApiKey("POS", "test-key", 30, []);
        cache.Set(("owner", receipt), new PendingApiKey(issued));
        var controller = new ApiAccessController(null!, new StaffPinAccess(), cache)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        void SignIn(string id) => controller.HttpContext.User = new ClaimsPrincipal(
            new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, id)], "test"));

        SignIn("another-admin");
        Assert.IsType<RedirectToActionResult>(controller.Issued(receipt));
        SignIn("owner");
        Assert.Same(issued, Assert.IsType<ViewResult>(controller.Issued(receipt)).Model);
        Assert.Equal("Keys", Assert.IsType<RedirectToActionResult>(controller.Issued(receipt)).ActionName);
    }
}
