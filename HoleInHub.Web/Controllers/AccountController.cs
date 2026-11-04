using System.Text;
using HoleInHub.Business.Accounts;
using HoleInHub.Web.Accounts;
using HoleInHub.Web.ViewModels;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace HoleInHub.Web.Controllers;

[Route("account"), EnableRateLimiting("accounts"), AutoValidateAntiforgeryToken]
public sealed partial class AccountController(UserManager<AccountUser> users, SignInManager<AccountUser> signIn,
    NotificationService notifications, IOptions<AccountEmailOptions> emailOptions, IHostEnvironment environment,
    AccountService accounts, IAuthenticationSchemeProvider schemes, EmailVerificationService verification,
    IDataProtectionProvider protection, TimeProvider clock) : Controller
{
    [HttpGet("sign-up")]
    public IActionResult SignUp(string? returnUrl) => User.Identity?.IsAuthenticated == true
        ? LocalRedirect(SafeReturnUrl(returnUrl) ?? "/my-reservations") : View(new SignUpModel { ReturnUrl = SafeReturnUrl(returnUrl) });

    [HttpPost("sign-up")]
    public async Task<IActionResult> SignUp(SignUpModel model, CancellationToken ct)
    {
        model.ReturnUrl = SafeReturnUrl(model.ReturnUrl);
        if (User.Identity?.IsAuthenticated == true) { return LocalRedirect(model.ReturnUrl ?? "/my-reservations"); }
        ModelState.Remove("ReturnUrl");
        if (!ModelState.IsValid) { return View(model); }
        var user = new AccountUser { Email = model.Email.Trim(), FirstName = model.FirstName.Trim(), LastName = model.LastName.Trim(), Phone = model.Phone.Trim() };
        var result = await users.CreateAsync(user, model.Password);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors) { ModelState.AddModelError("", error.Description); }
            return View(model);
        }
        await QueueLinkAsync(user, false, model.ReturnUrl, ct);
        return RedirectToAction(nameof(VerifyEmail), new { returnUrl = model.ReturnUrl });
    }

    [HttpGet("sign-in")]
    public IActionResult SignIn(string? returnUrl) => View(new SignInModel { ReturnUrl = SafeReturnUrl(returnUrl) });

    [HttpPost("sign-in")]
    public async Task<IActionResult> SignIn(SignInModel model)
    {
        model.ReturnUrl = SafeReturnUrl(model.ReturnUrl);
        ModelState.Remove("ReturnUrl");
        if (!ModelState.IsValid) { return View(model); }
        var user = await users.FindByEmailAsync(model.Email.Trim());
        if (user is not null)
        {
            var result = await signIn.PasswordSignInAsync(user, model.Password, false, true);
            if (result.Succeeded)
            {
                if (!user.EmailConfirmed)
                {
                    await EnsureAccountChallengeAsync(user, model.ReturnUrl, HttpContext.RequestAborted);
                    return RedirectToAction(nameof(VerifyEmail), new { returnUrl = model.ReturnUrl });
                }
                TempData["PageArrival"] = true;
                return LocalRedirect(Url.IsLocalUrl(model.ReturnUrl) ? model.ReturnUrl! : "/");
            }
        }
        ModelState.AddModelError("", "Unable to sign in. Check your details or try again later.");
        return View(model);
    }

    [HttpPost("sign-out")]
    public async Task<IActionResult> SignOutAccount()
    {
        await signIn.SignOutAsync();
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
        var user = await users.FindByEmailAsync(model.Email.Trim());
        if (user is not null && user.EmailConfirmed) { await QueueLinkAsync(user, true, model.ReturnUrl, ct); }
        TempData["AccountMessage"] = "If an eligible account exists, password-reset instructions have been queued for delivery.";
        return RedirectToAction(nameof(ForgotPassword), new { returnUrl = model.ReturnUrl });
    }

    [HttpGet("reset-password")]
    public IActionResult ResetPassword(string? userId, string? token, string? returnUrl) => View(new ResetPasswordModel { UserId = userId ?? "", Token = token ?? "", ReturnUrl = SafeReturnUrl(returnUrl) });

    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword(ResetPasswordModel model)
    {
        model.ReturnUrl = SafeReturnUrl(model.ReturnUrl);
        ModelState.Remove("ReturnUrl");
        if (!ModelState.IsValid) { return View(model); }
        var user = await users.FindByIdAsync(model.UserId);
        var decoded = Decode(model.Token);
        if (user is not null && decoded is not null && user.EmailConfirmed)
        {
            var result = await users.ResetPasswordAsync(user, decoded, model.Password);
            if (result.Succeeded)
            {
                TempData["AccountMessage"] = "Your password has been reset. Sign in with your new password.";
                return RedirectToAction(nameof(SignIn), new { returnUrl = model.ReturnUrl });
            }
        }
        ModelState.AddModelError("", "This reset link is invalid or expired. Request a new link.");
        return View(model);
    }

    private async Task QueueLinkAsync(AccountUser user, bool reset, string? returnUrl, CancellationToken ct)
    {
        if (!reset) { await BeginAccountChallengeAsync(user, returnUrl, ct); return; }
        var baseUrl = emailOptions.Value.PublicBaseUrl;
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) || (!environment.IsDevelopment() && uri.Scheme != "https"))
        {
            throw new InvalidOperationException("Configure AccountEmail:PublicBaseUrl with the trusted application address.");
        }
        var token = reset ? await users.GeneratePasswordResetTokenAsync(user) : await users.GenerateEmailConfirmationTokenAsync(user);
        var encoded = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
        var link = QueryHelpers.AddQueryString(new Uri(uri, reset ? "/account/reset-password" : "/account/verify-email").AbsoluteUri, new Dictionary<string, string?> { ["userId"] = user.Id, ["token"] = encoded, ["returnUrl"] = SafeReturnUrl(returnUrl) });
        await notifications.QueueAsync(user.Email, reset ? "Reset your Hole In Hub password" : "Verify your Hole In Hub email", $"{(reset ? "Reset your password" : "Verify your email")} using this link within two hours:\n\n{link}\n\nIf you did not request this, ignore this email.", ct);
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
