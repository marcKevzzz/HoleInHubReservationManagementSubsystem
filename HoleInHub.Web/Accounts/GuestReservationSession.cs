using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace HoleInHub.Web.Accounts;

public sealed class GuestReservationSession(IDataProtectionProvider protection, TimeProvider clock, IHostEnvironment environment)
{
    public const string AccessCookie = "hih-guest-access";
    public const string ChallengeCookie = "hih-guest-recovery";
    public sealed record Access(string Email, DateTimeOffset IssuedUtc, DateTimeOffset ExpiresUtc);
    public sealed record Challenge(string Id, string Email, string ReturnUrl, DateTimeOffset IssuedUtc);

    public string? Email(HttpContext http)
    {
        var access = Read<Access>(http, AccessCookie);
        var now = clock.GetUtcNow();
        return access is not null && access.IssuedUtc <= now && access.ExpiresUtc > now &&
            access.ExpiresUtc - access.IssuedUtc == TimeSpan.FromMinutes(30) ? access.Email : null;
    }

    public Challenge? Recovery(HttpContext http)
    {
        var challenge = Read<Challenge>(http, ChallengeCookie);
        return challenge is not null && challenge.IssuedUtc <= clock.GetUtcNow() &&
            clock.GetUtcNow() - challenge.IssuedUtc < TimeSpan.FromHours(2) ? challenge : null;
    }

    public void Begin(HttpContext http, Challenge challenge) => Write(http, ChallengeCookie, challenge, TimeSpan.FromHours(2), "/guest/reservations");
    public void Grant(HttpContext http, string email)
    {
        var now = clock.GetUtcNow();
        Write(http, AccessCookie, new Access(email, now, now.AddMinutes(30)), TimeSpan.FromMinutes(30), "/");
        http.Response.Cookies.Delete(ChallengeCookie, new CookieOptions { Path = "/guest/reservations" });
    }
    public void Leave(HttpContext http)
    {
        http.Response.Cookies.Delete(AccessCookie, new CookieOptions { Path = "/" });
        http.Response.Cookies.Delete(ChallengeCookie, new CookieOptions { Path = "/guest/reservations" });
    }
    private T? Read<T>(HttpContext http, string name)
    {
        var value = http.Request.Cookies[name];
        if (string.IsNullOrEmpty(value) || value.Length > 8192) { return default; }
        try { return JsonSerializer.Deserialize<T>(protection.CreateProtector("GuestReservationSession.v1", name).Unprotect(value)); }
        catch (Exception error) when (error is CryptographicException or JsonException) { return default; }
    }
    private void Write<T>(HttpContext http, string name, T value, TimeSpan lifetime, string path) =>
        http.Response.Cookies.Append(name, protection.CreateProtector("GuestReservationSession.v1", name).Protect(JsonSerializer.Serialize(value)),
            new CookieOptions { HttpOnly = true, Secure = !environment.IsDevelopment() || http.Request.IsHttps, SameSite = SameSiteMode.Lax, MaxAge = lifetime, Path = path, IsEssential = true });
}
