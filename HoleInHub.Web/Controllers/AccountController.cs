using System.Security.Claims;
using System.Text;
using HoleInHub.Business.Accounts;
using HoleInHub.Web.Accounts;
using HoleInHub.Web.ViewModels;
using HoleInHubReservationManagementSubsystem.Controllers;
using HoleInHubReservationManagementSubsystem.ViewModels;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.WebUtilities;

namespace HoleInHub.Web.Controllers;

[Route("account"), EnableRateLimiting("accounts"), AutoValidateAntiforgeryToken, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed partial class AccountController(UserManager<AccountUser> users, SignInManager<AccountUser> signIn,
    IHostEnvironment environment,
    AccountService accounts, IAuthenticationSchemeProvider schemes, EmailVerificationService verification,
    IDataProtectionProvider protection, TimeProvider clock) : Controller
{
    [HttpGet("sign-up")]
    public async Task<IActionResult> SignUp(string? returnUrl) => await SignedInDestinationAsync(returnUrl) is { } destination
        ? LocalRedirect(destination) : View(new SignUpModel { ReturnUrl = SafeReturnUrl(returnUrl) });

    [HttpPost("sign-up")]
    public async Task<IActionResult> SignUp(SignUpModel model, CancellationToken ct)
    {
        model.ReturnUrl = SafeReturnUrl(model.ReturnUrl);
        if (await SignedInDestinationAsync(model.ReturnUrl) is { } destination) { return LocalRedirect(destination); }
        ModelState.Remove("ReturnUrl");
        if (!ModelState.IsValid) { return View(model); }
        var user = new AccountUser { Email = model.Email.Trim(), FirstName = model.FirstName.Trim(), LastName = model.LastName.Trim(), Phone = ContactRules.PhoneDigits(model.Phone) };
        var result = await users.CreateAsync(user, model.Password);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors) { ModelState.AddModelError("", error.Description); }
            return View(model);
        }
        await BeginAccountChallengeAsync(user, model.ReturnUrl, ct);
        new ToastNotice("success", "Account created", "Check your email to finish verification.").Store(TempData);
        return RedirectToAction(nameof(VerifyEmail), new { returnUrl = model.ReturnUrl });
    }

    [HttpGet("sign-in")]
    public async Task<IActionResult> SignIn(string? returnUrl) => await SignedInDestinationAsync(returnUrl) is { } destination
        ? LocalRedirect(destination) : View(new SignInModel { ReturnUrl = SafeReturnUrl(returnUrl) });

    [HttpPost("sign-in")]
    public async Task<IActionResult> SignIn(SignInModel model)
    {
        model.ReturnUrl = SafeReturnUrl(model.ReturnUrl);
        if (await SignedInDestinationAsync(model.ReturnUrl) is { } signedInDestination) { return LocalRedirect(signedInDestination); }
        ModelState.Remove("ReturnUrl");
        if (!ModelState.IsValid) { return View(model); }
        var user = await users.FindByEmailAsync(model.Email.Trim());
        if (user is not null)
        {
            var result = await signIn.CheckPasswordSignInAsync(user, model.Password, true);
            if (result.Succeeded)
            {
                await signIn.SignInWithClaimsAsync(user, CustomerSession(model.RememberMe && !user.Roles.Any(StaffRoles.Workspace.Contains)),
                    [new Claim("amr", "pwd")]);
                if (!user.EmailConfirmed)
                {
                    await EnsureAccountChallengeAsync(user, model.ReturnUrl, HttpContext.RequestAborted, model.RememberMe);
                    return RedirectToAction(nameof(VerifyEmail), new { returnUrl = model.ReturnUrl });
                }
                var isAdmin = user.Roles.Contains(StaffRoles.Admin);
                if (isAdmin)
                {
                    TempData["ResetStaffScroll"] = true;
                    await StaffPinAccess.SignInAsync(HttpContext, user, clock.GetUtcNow(),
                    [
                        new Claim(ClaimTypes.Role, StaffRoles.Admin),
                        new Claim(ClaimTypes.AuthenticationMethod, StaffPinAccess.AdminPasswordMethod)
                    ]);
                }
                TempData["PageArrival"] = true;
                new ToastNotice("success", user.WelcomeTitle, "You’re signed in.", Icon: "sign-in").Store(TempData);
                if (model.ReturnUrl is { } destination) { return LocalRedirect(destination); }
                return isAdmin ? RedirectToAction(nameof(LeadershipController.Dashboard), "Leadership") : LocalRedirect("/");
            }
        }
        ModelState.AddModelError("", "Unable to sign in. Check your details or try again later.");
        return View(model);
    }

    [HttpPost("sign-out")]
    public async Task<IActionResult> SignOutAccount()
    {
        await HttpContext.SignOutAsync(StaffPinAccess.Scheme);
        await signIn.SignOutAsync();
        new ToastNotice("success", "Signed out", "See you next time.", Icon: "sign-out").Store(TempData);
        return Redirect("/");
    }

    [HttpGet("forgot-password")]
    public IActionResult ForgotPassword(string? returnUrl) => View(new EmailModel { ReturnUrl = SafeReturnUrl(returnUrl) });

    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword(EmailModel model, CancellationToken ct)
    {
        model.ReturnUrl = SafeReturnUrl(model.ReturnUrl);
        ModelState.Remove("ReturnUrl");
        if (!ModelState.IsValid) { return View(model); }
        try { await BeginPasswordResetAsync(model.Email.Trim(), model.ReturnUrl, ct); }
        catch (HoleInHub.Business.Reservations.ReservationValidationException error)
        {
            ModelState.AddModelError("", error.Message);
            return View(model);
        }
        return RedirectToAction(nameof(VerifyReset));
    }

    [HttpGet("reset-password")]
    public async Task<IActionResult> ResetPassword(string? userId, string? token, string? returnUrl, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(token))
        {
            var proof = await PasswordResetProofAsync(ct);
            if (proof is null) { return RedirectToAction(nameof(ForgotPassword), new { returnUrl = SafeReturnUrl(returnUrl) }); }
            userId = "code"; token = "code"; returnUrl = proof.Value.ReturnUrl;
        }
        return View(new ResetPasswordModel { UserId = userId, Token = token, ReturnUrl = SafeReturnUrl(returnUrl) });
    }

    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword(ResetPasswordModel model)
    {
        model.ReturnUrl = SafeReturnUrl(model.ReturnUrl);
        ModelState.Remove("ReturnUrl");
        if (!ModelState.IsValid) { return View(model); }
        if (model.UserId == "code" && model.Token == "code")
        {
            var proof = await PasswordResetProofAsync(HttpContext.RequestAborted);
            if (proof is null) { return RedirectToAction(nameof(ForgotPassword)); }
            model.UserId = proof.Value.UserId; model.Token = proof.Value.Token;
        }
        var user = await users.FindByIdAsync(model.UserId);
        var decoded = Decode(model.Token);
        if (user is not null && decoded is not null && user.EmailConfirmed)
        {
            var result = await users.ResetPasswordAsync(user, decoded, model.Password);
            if (result.Succeeded)
            {
                Response.Cookies.Delete(ResetCookie, new CookieOptions { Path = "/account" });
                TempData["AccountMessage"] = "Your password has been reset. Sign in with your new password.";
                return RedirectToAction(nameof(SignIn), new { returnUrl = model.ReturnUrl });
            }
        }
        ModelState.AddModelError("", "This password reset is invalid or expired. Request a new code.");
        return View(model);
    }

    private AuthenticationProperties CustomerSession(bool remember) => new(new Dictionary<string, string?>
    {
        ["hih-session-end"] = clock.GetUtcNow().Add(remember ? TimeSpan.FromDays(30) : TimeSpan.FromHours(8)).ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture)
    })
    {
        IsPersistent = remember,
        ExpiresUtc = clock.GetUtcNow().Add(remember ? TimeSpan.FromDays(30) : TimeSpan.FromHours(8)),
        AllowRefresh = false
    };
    private async Task<string?> SignedInDestinationAsync(string? returnUrl)
    {
        var destination = SafeReturnUrl(returnUrl);
        var path = destination?.Split('?')[0].TrimEnd('/');
        if (path is not null && (path.Equals("/account/sign-in", StringComparison.OrdinalIgnoreCase) || path.Equals("/account/sign-up", StringComparison.OrdinalIgnoreCase))) { destination = null; }
        var workspace = await HttpContext.AuthenticateAsync(StaffPinAccess.Scheme);
        if (workspace.Principal?.Identity?.IsAuthenticated == true && StaffRoles.Workspace.Any(workspace.Principal.IsInRole))
        {
            return destination ?? (workspace.Principal.ManagesVenue() ? "/staff/dashboard" : "/staff");
        }
        if (User.Identity?.IsAuthenticated != true) { return null; }
        // A valid password session can reopen an admin workspace after its shorter cookie expires.
        if (User.IsInRole(StaffRoles.Admin) && User.HasClaim("amr", "pwd") && await users.GetUserAsync(User) is { EmailConfirmed: true } admin
            && admin.Roles.Contains(StaffRoles.Admin) && (admin.LockoutEnd is null || admin.LockoutEnd <= clock.GetUtcNow())
            && admin.SecurityStamp == User.FindFirstValue(users.Options.ClaimsIdentity.SecurityStampClaimType))
        {
            await StaffPinAccess.SignInAsync(HttpContext, admin, clock.GetUtcNow(),
                [new Claim(ClaimTypes.Role, StaffRoles.Admin), new Claim(ClaimTypes.AuthenticationMethod, StaffPinAccess.AdminPasswordMethod)]);
            return destination ?? "/staff/dashboard";
        }
        return destination ?? (User.HasClaim("email_verified", "true") ? "/my-reservations" : "/account/verify-email");
    }
    private string? SafeReturnUrl(string? value)
    {
        ModelState.Remove("ReturnUrl");
        return value is { Length: <= 2048 } && Url.IsLocalUrl(value) ? value : null;
    }

    private static string? Decode(string token)
    {
        try { return Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(token)); }
        catch (FormatException) { return null; }
    }
}
