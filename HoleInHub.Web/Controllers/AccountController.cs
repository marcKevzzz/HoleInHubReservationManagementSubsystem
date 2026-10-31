using System.Text;
using HoleInHub.Business.Accounts;
using HoleInHub.Web.Accounts;
using HoleInHub.Web.ViewModels;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace HoleInHub.Web.Controllers;

[Route("account"), EnableRateLimiting("accounts"), AutoValidateAntiforgeryToken]
public sealed class AccountController(UserManager<AccountUser> users, SignInManager<AccountUser> signIn,
    NotificationService notifications, IOptions<AccountEmailOptions> emailOptions, IHostEnvironment environment) : Controller
{
    [HttpGet("sign-up")]
    public IActionResult SignUp() => View(new SignUpModel());

    [HttpPost("sign-up")]
    public async Task<IActionResult> SignUp(SignUpModel model, CancellationToken ct)
    {
        if (!ModelState.IsValid) { return View(model); }
        var user = new AccountUser { Email = model.Email.Trim() };
        var result = await users.CreateAsync(user, model.Password);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors) { ModelState.AddModelError("", error.Description); }
            return View(model);
        }
        await QueueLinkAsync(user, false, ct);
        TempData["AccountMessage"] = "Your account was created. You can sign in and browse now. Verify your email before submitting a reservation.";
        return RedirectToAction(nameof(VerifyEmail));
    }

    [HttpGet("sign-in")]
    public IActionResult SignIn(string? returnUrl) => View(new SignInModel { ReturnUrl = Url.IsLocalUrl(returnUrl) ? returnUrl : null });

    [HttpPost("sign-in")]
    public async Task<IActionResult> SignIn(SignInModel model)
    {
        if (!ModelState.IsValid) { return View(model); }
        var user = await users.FindByEmailAsync(model.Email.Trim());
        if (user is not null)
        {
            var result = await signIn.PasswordSignInAsync(user, model.Password, false, true);
            if (result.Succeeded) { return LocalRedirect(Url.IsLocalUrl(model.ReturnUrl) ? model.ReturnUrl! : "/my-reservations"); }
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

    [HttpGet("verify-email")]
    public async Task<IActionResult> VerifyEmail(string? userId, string? token)
    {
        if (userId?.Length > 64 || token?.Length > 4096) { return BadRequest(); }
        if (!string.IsNullOrWhiteSpace(userId) && !string.IsNullOrWhiteSpace(token))
        {
            var user = await users.FindByIdAsync(userId);
            var decoded = Decode(token);
            if (user is not null && !user.EmailConfirmed && decoded is not null && (await users.ConfirmEmailAsync(user, decoded)).Succeeded)
            {
                if (User.Identity?.IsAuthenticated == true && users.GetUserId(User) == user.Id) { await signIn.RefreshSignInAsync(user); }
                ViewData["Message"] = "Your email is verified. You can now submit reservation requests.";
            }
            else { ViewData["Message"] = "This verification link is invalid, expired, or already used. Sign in or request another link."; }
        }
        return View(new EmailModel());
    }

    [HttpPost("verify-email")]
    public async Task<IActionResult> ResendVerification(EmailModel model, CancellationToken ct)
    {
        if (!ModelState.IsValid) { return View("VerifyEmail", model); }
        var user = await users.FindByEmailAsync(model.Email.Trim());
        if (user is not null && !user.EmailConfirmed) { await QueueLinkAsync(user, false, ct); }
        TempData["AccountMessage"] = "If this account needs verification, a link will be sent to its inbox.";
        return RedirectToAction(nameof(VerifyEmail));
    }

    [HttpGet("forgot-password")]
    public IActionResult ForgotPassword() => View(new EmailModel());

    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword(EmailModel model, CancellationToken ct)
    {
        if (!ModelState.IsValid) { return View(model); }
        var user = await users.FindByEmailAsync(model.Email.Trim());
        if (user is not null && user.EmailConfirmed) { await QueueLinkAsync(user, true, ct); }
        TempData["AccountMessage"] = "If an eligible account exists, password-reset instructions will be sent to its inbox.";
        return RedirectToAction(nameof(ForgotPassword));
    }

    [HttpGet("reset-password")]
    public IActionResult ResetPassword(string? userId, string? token) => View(new ResetPasswordModel { UserId = userId ?? "", Token = token ?? "" });

    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword(ResetPasswordModel model)
    {
        if (!ModelState.IsValid) { return View(model); }
        var user = await users.FindByIdAsync(model.UserId);
        var decoded = Decode(model.Token);
        if (user is not null && decoded is not null && user.EmailConfirmed)
        {
            var result = await users.ResetPasswordAsync(user, decoded, model.Password);
            if (result.Succeeded)
            {
                TempData["AccountMessage"] = "Your password has been reset. Sign in with your new password.";
                return RedirectToAction(nameof(SignIn));
            }
        }
        ModelState.AddModelError("", "This reset link is invalid or expired. Request a new link.");
        return View(model);
    }

    private async Task QueueLinkAsync(AccountUser user, bool reset, CancellationToken ct)
    {
        var baseUrl = emailOptions.Value.PublicBaseUrl;
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) || (!environment.IsDevelopment() && uri.Scheme != "https"))
        {
            throw new InvalidOperationException("Configure AccountEmail:PublicBaseUrl with the trusted application address.");
        }
        var token = reset ? await users.GeneratePasswordResetTokenAsync(user) : await users.GenerateEmailConfirmationTokenAsync(user);
        var encoded = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
        var link = QueryHelpers.AddQueryString(new Uri(uri, reset ? "/account/reset-password" : "/account/verify-email").AbsoluteUri, new Dictionary<string, string?> { ["userId"] = user.Id, ["token"] = encoded });
        await notifications.QueueAsync(user.Email, reset ? "Reset your Hole In Hub password" : "Verify your Hole In Hub email", $"{(reset ? "Reset your password" : "Verify your email")} using this link within two hours:\n\n{link}\n\nIf you did not request this, ignore this email.", ct);
    }
    private static string? Decode(string token)
    {
        try { return Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(token)); }
        catch (FormatException) { return null; }
    }
}
