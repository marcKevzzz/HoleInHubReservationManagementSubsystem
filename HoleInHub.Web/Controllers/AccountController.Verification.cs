using System.Security.Cryptography;
using System.Text.Json;
using HoleInHub.Business.Reservations;
using HoleInHub.Web.Accounts;
using HoleInHub.Web.ViewModels;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;

namespace HoleInHub.Web.Controllers;

public sealed partial class AccountController
{
    private sealed record AccountVerificationContext(string Session, string UserId, string? ReturnUrl, DateTimeOffset CreatedUtc);
    private const string VerificationCookie = "hih-account-email";

    [HttpGet("verify-email")]
    public async Task<IActionResult> VerifyEmail(string? userId, string? token, string? returnUrl, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        if (userId?.Length > 64 || token?.Length > 4096) { return BadRequest(); }
        if (!string.IsNullOrWhiteSpace(userId) && !string.IsNullOrWhiteSpace(token))
        {
            var linkedUser = await users.FindByIdAsync(userId); var decoded = Decode(token);
            if (linkedUser is not null && !linkedUser.EmailConfirmed && decoded is not null && (await users.ConfirmEmailAsync(linkedUser, decoded)).Succeeded)
            {
                if (users.GetUserId(User) == linkedUser.Id) { await signIn.RefreshSignInAsync(linkedUser); }
                ViewData["Message"] = "Your email is verified.";
                return View(new AccountVerificationViewModel { Complete = true, ReturnUrl = SafeReturnUrl(returnUrl), Email = linkedUser.Email });
            }
            ModelState.AddModelError("", "This verification link is invalid or expired.");
        }
        var context = ReadAccountContext();
        var user = User.Identity?.IsAuthenticated == true ? await users.GetUserAsync(User) : context is not null ? await users.FindByIdAsync(context.UserId) : null;
        if (user is null) { return View(new AccountVerificationViewModel { ReturnUrl = SafeReturnUrl(returnUrl) }); }
        if (user.EmailConfirmed) { return View(new AccountVerificationViewModel { Complete = true, Email = user.Email, ReturnUrl = SafeReturnUrl(returnUrl ?? context?.ReturnUrl) }); }
        if (context?.UserId != user.Id) { context = null; }
        return View(await AccountCodeModelAsync(user, context, SafeReturnUrl(returnUrl ?? context?.ReturnUrl), ct));
    }

    [HttpPost("verify-email")]
    public async Task<IActionResult> ResendVerification(string? code, string? intent, string? returnUrl, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var context = ReadAccountContext();
        var user = User.Identity?.IsAuthenticated == true ? await users.GetUserAsync(User) : context is not null ? await users.FindByIdAsync(context.UserId) : null;
        if (user is null) { return View("VerifyEmail", new AccountVerificationViewModel { ReturnUrl = SafeReturnUrl(returnUrl) }); }
        if (user.EmailConfirmed) { return RedirectToAction(nameof(VerifyEmail), new { returnUrl = SafeReturnUrl(returnUrl ?? context?.ReturnUrl) }); }
        if (context?.UserId != user.Id) { context = null; }
        returnUrl = SafeReturnUrl(returnUrl ?? context?.ReturnUrl);
        try
        {
            if (intent == "resend")
            {
                await BeginAccountChallengeAsync(user, returnUrl, ct, context);
                context = ReadAccountContext();
                ViewData["VerificationMessage"] = "A new code is on its way.";
            }
            else if (context is not null)
            {
                var challenge = await verification.ReadAsync("account-email:" + context.Session, ct);
                if (challenge?.Email != user.Email || challenge.ProtectedContext is null || challenge.ExpiresUtc <= clock.GetUtcNow().UtcDateTime) { throw new ReservationValidationException("Your code expired. Resend a code."); }
                if (!challenge.Verified) { await verification.VerifyAsync("account-email:" + context.Session, user.Email, code ?? "", ct); }
                var identityToken = protection.CreateProtector("AccountEmailCode", user.Id).Unprotect(challenge.ProtectedContext);
                if (!(await users.ConfirmEmailAsync(user, identityToken)).Succeeded) { throw new ReservationValidationException("Your code expired. Resend a code."); }
                await signIn.SignInAsync(user, false);
                Response.Cookies.Delete(VerificationCookie, new CookieOptions { Path = "/account" });
                TempData["PageArrival"] = true;
                return LocalRedirect("/");
            }
            else { throw new ReservationValidationException("Resend a code to continue."); }
        }
        catch (ReservationValidationException error) { ModelState.AddModelError("", error.Message); }
        catch (CryptographicException) { ModelState.AddModelError("", "Your code expired. Resend a code."); }
        return View("VerifyEmail", await AccountCodeModelAsync(user, context, returnUrl, ct));
    }

    private AccountVerificationContext? ReadAccountContext()
    {
        var value = Request.Cookies[VerificationCookie];
        if (string.IsNullOrEmpty(value) || value.Length > 8192) { return null; }
        try
        {
            var context = JsonSerializer.Deserialize<AccountVerificationContext>(protection.CreateProtector("AccountEmailSession").Unprotect(value));
            return context is not null && context.CreatedUtc <= clock.GetUtcNow() && clock.GetUtcNow() - context.CreatedUtc < TimeSpan.FromHours(2) ? context : null;
        }
        catch (Exception error) when (error is CryptographicException or JsonException) { return null; }
    }

    private async Task EnsureAccountChallengeAsync(AccountUser user, string? returnUrl, CancellationToken ct)
    {
        var context = ReadAccountContext();
        if (context?.UserId == user.Id && await verification.ReadAsync("account-email:" + context.Session, ct) is { } challenge && challenge.ExpiresUtc > clock.GetUtcNow().UtcDateTime) { return; }
        await BeginAccountChallengeAsync(user, returnUrl, ct, context?.UserId == user.Id ? context : null);
    }

    private async Task BeginAccountChallengeAsync(AccountUser user, string? returnUrl, CancellationToken ct, AccountVerificationContext? context = null)
    {
        context ??= new(Convert.ToHexString(RandomNumberGenerator.GetBytes(32)), user.Id, SafeReturnUrl(returnUrl), clock.GetUtcNow());
        var scope = "account-email:" + context.Session; var code = RandomNumberGenerator.GetInt32(100000, 1000000).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var body = "protected:" + protection.CreateProtector("BookingEmail").Protect($"Your Hole In Hub account verification code is {code}. It expires in 10 minutes. Do not share it.");
        var identityToken = protection.CreateProtector("AccountEmailCode", user.Id).Protect(await users.GenerateEmailConfirmationTokenAsync(user));
        await verification.IssueAsync(scope, user.Email, HoleInHub.Business.Accounts.EmailVerificationService.CodeHash(scope, user.Email, code), body, "Verify your Hole In Hub email", "AccountEmail", ct, identityToken);
        var value = protection.CreateProtector("AccountEmailSession").Protect(JsonSerializer.Serialize(context));
        Response.Cookies.Append(VerificationCookie, value, new CookieOptions { Path = "/account", HttpOnly = true, Secure = !environment.IsDevelopment() || Request.IsHttps, SameSite = SameSiteMode.Lax, MaxAge = TimeSpan.FromHours(2), IsEssential = true });
        // Read the just-issued context when this request renders after a resend.
        HttpContext.Items[VerificationCookie] = context;
    }

    private async Task<AccountVerificationViewModel> AccountCodeModelAsync(AccountUser user, AccountVerificationContext? context, string? returnUrl, CancellationToken ct)
    {
        context = HttpContext.Items[VerificationCookie] as AccountVerificationContext ?? context;
        var challenge = context is not null ? await verification.ReadAsync("account-email:" + context.Session, ct) : null;
        return new()
        {
            HasContext = true,
            Email = user.Email,
            ActionUrl = "/account/verify-email",
            SubmitLabel = "Verify email",
            ReturnUrl = returnUrl,
            ExpiresUtc = challenge?.ExpiresUtc ?? clock.GetUtcNow().UtcDateTime,
            ResendAfterUtc = challenge?.ResendAfterUtc ?? clock.GetUtcNow().UtcDateTime,
            ServerNowUtc = clock.GetUtcNow().UtcDateTime,
            Expired = challenge is null || challenge.ExpiresUtc <= clock.GetUtcNow().UtcDateTime,
            ResendWhenExpired = true
        };
    }
}
