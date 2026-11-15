using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HoleInHub.Business.Accounts;
using HoleInHub.Business.Reservations;
using HoleInHub.Web.ViewModels;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;

namespace HoleInHub.Web.Controllers;

public sealed partial class AccountController
{
    private const string ResetCookie = "hih-password-reset";
    private sealed record PasswordResetContext(string Session, string Email, string? UserId, string? ReturnUrl, DateTimeOffset CreatedUtc);
    private static string ResetScope(PasswordResetContext context) => "password-reset:" + context.Session;

    private PasswordResetContext? ReadResetContext()
    {
        var value = Request.Cookies[ResetCookie];
        if (string.IsNullOrEmpty(value) || value.Length > 8192) { return null; }
        try
        {
            var context = JsonSerializer.Deserialize<PasswordResetContext>(protection.CreateProtector("PasswordResetSession").Unprotect(value));
            return context is not null && context.CreatedUtc <= clock.GetUtcNow()
                && clock.GetUtcNow() - context.CreatedUtc < TimeSpan.FromMinutes(30) ? context : null;
        }
        catch (Exception error) when (error is CryptographicException or JsonException) { return null; }
    }

    private async Task BeginPasswordResetAsync(string email, string? returnUrl, CancellationToken ct)
    {
        var user = await users.FindByEmailAsync(email);
        var existing = ReadResetContext();
        var context = existing is not null && string.Equals(existing.Email, email, StringComparison.OrdinalIgnoreCase)
            ? existing : new PasswordResetContext(Convert.ToHexString(RandomNumberGenerator.GetBytes(32)), user?.Email ?? email,
                user is { EmailConfirmed: true } ? user.Id : null, SafeReturnUrl(returnUrl), clock.GetUtcNow());
        await SendResetCodeAsync(context, ct);
        Response.Cookies.Append(ResetCookie, protection.CreateProtector("PasswordResetSession").Protect(JsonSerializer.Serialize(context)),
            new CookieOptions
            {
                Path = "/account",
                HttpOnly = true,
                Secure = !environment.IsDevelopment() || Request.IsHttps,
                SameSite = SameSiteMode.Lax,
                MaxAge = TimeSpan.FromMinutes(30),
                IsEssential = true
            });
    }

    private async Task SendResetCodeAsync(PasswordResetContext context, CancellationToken ct)
    {
        var user = context.UserId is not null ? await users.FindByIdAsync(context.UserId) : null;
        // Unknown and unverified accounts follow the same browser flow without disclosing account existence.
        if (user is not { EmailConfirmed: true } || !string.Equals(user.Email, context.Email, StringComparison.OrdinalIgnoreCase)) { return; }
        var code = RandomNumberGenerator.GetInt32(100000, 1000000).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var body = "protected:" + protection.CreateProtector("BookingEmail").Protect($"Your Hole In Hub password reset code is {code}. It expires in 10 minutes. Do not share it. If you did not request this, ignore this email.");
        var token = protection.CreateProtector("PasswordResetCode", user.Id).Protect(await users.GeneratePasswordResetTokenAsync(user));
        try
        {
            await verification.IssueAsync(ResetScope(context), user.Email, EmailVerificationService.CodeHash(ResetScope(context), user.Email, code),
                body, "Reset your Hole In Hub password", "AccountEmail", ct, token);
        }
        catch (ReservationValidationException)
        {
            // Delivery quotas must not reveal whether the submitted email has an account.
        }
    }

    [HttpGet("reset-password/verify")]
    public async Task<IActionResult> VerifyReset(CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var context = ReadResetContext();
        return context is null ? RedirectToAction(nameof(ForgotPassword)) : View(await ResetCodeModelAsync(context, ct));
    }

    [HttpPost("reset-password/verify")]
    public async Task<IActionResult> VerifyReset(string? code, string? intent, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var context = ReadResetContext();
        if (context is null) { return RedirectToAction(nameof(ForgotPassword)); }
        try
        {
            if (intent == "resend")
            {
                await SendResetCodeAsync(context, ct);
                ViewData["VerificationMessage"] = "If your account is eligible, a code is on its way. Please wait one minute between requests; you can request up to five per hour.";
            }
            else
            {
                await verification.VerifyAsync(ResetScope(context), context.Email, code ?? "", ct, "AccountEmail");
                if (await PasswordResetProofAsync(ct) is null) { throw new ReservationValidationException("This code is invalid or expired. Request a new code."); }
                return RedirectToAction(nameof(ResetPassword));
            }
        }
        catch (ReservationValidationException error) { ModelState.AddModelError("", error.Message); }
        return View(await ResetCodeModelAsync(context, ct));
    }

    private async Task<(string UserId, string Token, string? ReturnUrl)?> PasswordResetProofAsync(CancellationToken ct)
    {
        var context = ReadResetContext();
        if (context?.UserId is null) { return null; }
        var challenge = await verification.ReadAsync(ResetScope(context), ct);
        if (challenge is not { Verified: true, ProtectedContext: not null } || challenge.ExpiresUtc <= clock.GetUtcNow().UtcDateTime
            || !string.Equals(challenge.Email, context.Email, StringComparison.OrdinalIgnoreCase)) { return null; }
        try
        {
            var token = protection.CreateProtector("PasswordResetCode", context.UserId).Unprotect(challenge.ProtectedContext);
            return (context.UserId, WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token)), context.ReturnUrl);
        }
        catch (CryptographicException) { return null; }
    }

    private async Task<EmailCodeFormModel> ResetCodeModelAsync(PasswordResetContext context, CancellationToken ct)
    {
        var challenge = await verification.ReadAsync(ResetScope(context), ct);
        var now = clock.GetUtcNow().UtcDateTime;
        return new()
        {
            Email = context.Email,
            ActionUrl = "/account/reset-password/verify",
            SubmitLabel = "Verify code",
            ExpiresUtc = challenge?.ExpiresUtc ?? context.CreatedUtc.AddMinutes(10).UtcDateTime,
            ResendAfterUtc = challenge?.ResendAfterUtc ?? context.CreatedUtc.AddMinutes(1).UtcDateTime,
            ServerNowUtc = now,
            Expired = (challenge?.ExpiresUtc ?? context.CreatedUtc.AddMinutes(10).UtcDateTime) <= now,
            ResendWhenExpired = true
        };
    }
}
