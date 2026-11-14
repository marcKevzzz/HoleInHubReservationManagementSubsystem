using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;

namespace HoleInHubReservationManagementSubsystem.Controllers;

public sealed class LanguageController : Controller
{
    [HttpPost("/language"), ValidateAntiForgeryToken]
    public IActionResult Change(string culture, string? returnUrl)
    {
        if (culture is not ("en" or "ko")) { return BadRequest(); }
        Response.Cookies.Append(CookieRequestCultureProvider.DefaultCookieName,
            CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(culture)),
            new CookieOptions { Expires = DateTimeOffset.UtcNow.AddYears(1), HttpOnly = true, IsEssential = true,
                Secure = Request.IsHttps, SameSite = SameSiteMode.Lax, Path = "/" });
        return LocalRedirect(returnUrl is { Length: <= 2048 } && Url.IsLocalUrl(returnUrl) ? returnUrl : "/");
    }
}
