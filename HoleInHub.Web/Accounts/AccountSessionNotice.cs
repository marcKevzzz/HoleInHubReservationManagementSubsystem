using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;

namespace HoleInHub.Web.Accounts;

public static class AccountSessionNotice
{
    // This hint survives the authentication cookie so normal first-time visits stay quiet.
    public const string Cookie = "HihSessionNotice";
    public const string Admin = "admin";
    public static void Remember(HttpContext context, bool admin, bool persistent)
    {
        context.Response.Cookies.Append(Cookie, admin ? Admin : "customer", new CookieOptions
        {
            Path = "/", HttpOnly = true, Secure = context.Request.IsHttps, SameSite = SameSiteMode.Lax,
            MaxAge = persistent ? TimeSpan.FromDays(31) : null, IsEssential = true
        });
    }

    public static async Task<DateTimeOffset?> DeadlineAsync(HttpContext context, bool admin)
    {
        var account = await context.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        if (!account.Succeeded || account.Properties?.ExpiresUtc is not { } end) { return null; }
        if (!admin) { return end; }
        var workspace = await context.AuthenticateAsync(StaffPinAccess.Scheme);
        if (!workspace.Succeeded || workspace.Properties?.ExpiresUtc is not { } idleEnd) { return null; }
        return end < idleEnd ? end : idleEnd;
    }
}
