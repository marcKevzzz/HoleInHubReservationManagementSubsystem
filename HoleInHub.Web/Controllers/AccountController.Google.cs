using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using HoleInHub.Web.Accounts;
using HoleInHub.Web.ViewModels;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace HoleInHub.Web.Controllers;

public sealed partial class AccountController
{
    [HttpPost("google")]
    public async Task<IActionResult> Google(string? returnUrl)
    {
        if (await schemes.GetSchemeAsync("Google") is null) { return NotFound(); }
        await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
        var callback = Url.Action(nameof(GoogleCallback), new { returnUrl = SafeReturnUrl(returnUrl) })!;
        return Challenge(signIn.ConfigureExternalAuthenticationProperties("Google", callback), "Google");
    }

    [HttpGet("google-failed")]
    public async Task<IActionResult> GoogleFailed()
    {
        await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
        TempData["AccountMessage"] = "Google sign-in was cancelled or could not be completed. Try again or use email and password.";
        return RedirectToAction(nameof(SignIn));
    }

    [HttpGet("google-callback")]
    public async Task<IActionResult> GoogleCallback(string? returnUrl, CancellationToken ct)
    {
        var info = await GoogleInfoAsync();
        if (info is null) { return await GoogleFailed(); }
        var account = await accounts.FindByGoogleSubjectAsync(info.ProviderKey, ct);
        if (account is not null) { return await FinishGoogleAsync(AccountUser.From(account)!, SafeReturnUrl(returnUrl)); }
        var email = info.Principal.FindFirstValue(ClaimTypes.Email)!;
        if (await users.FindByEmailAsync(email) is not null) { return await ExistingGoogleEmailAsync(returnUrl); }
        return View("GoogleSignUp", new GoogleSignUpModel { Email = email, FirstName = Name(ClaimTypes.GivenName), LastName = Name(ClaimTypes.Surname), ReturnUrl = SafeReturnUrl(returnUrl) });
        string Name(string claim) { var value = info.Principal.FindFirstValue(claim) ?? ""; return value.Length <= 100 ? value : ""; }
    }

    [HttpPost("google-complete")]
    public async Task<IActionResult> GoogleComplete(GoogleSignUpModel model, CancellationToken ct)
    {
        var info = await GoogleInfoAsync();
        if (info is null) { return await GoogleFailed(); }
        model.ReturnUrl = SafeReturnUrl(model.ReturnUrl);
        model.Email = info.Principal.FindFirstValue(ClaimTypes.Email)!;
        ModelState.Remove(nameof(model.Email));
        if (!ModelState.IsValid) { return View("GoogleSignUp", model); }
        var existing = await accounts.FindByGoogleSubjectAsync(info.ProviderKey, ct);
        if (existing is not null) { return await FinishGoogleAsync(AccountUser.From(existing)!, model.ReturnUrl); }
        if (await users.FindByEmailAsync(model.Email) is not null) { return await ExistingGoogleEmailAsync(model.ReturnUrl); }
        var user = new AccountUser { Email = model.Email, GoogleSubject = info.ProviderKey, FirstName = model.FirstName.Trim(), LastName = model.LastName.Trim(), Phone = model.Phone.Trim(), EmailConfirmed = GoogleEmailIsAuthoritative(info.Principal) };
        if (!(await users.CreateAsync(user)).Succeeded)
        {
            ModelState.AddModelError("", "Unable to create this account. Retry Google sign-in or use your existing sign-in method.");
            return View("GoogleSignUp", model);
        }
        return await FinishGoogleAsync(user, model.ReturnUrl);
    }

    private async Task<ExternalLoginInfo?> GoogleInfoAsync()
    {
        if (await schemes.GetSchemeAsync("Google") is null) { return null; }
        var info = await signIn.GetExternalLoginInfoAsync();
        var email = info?.Principal.FindFirstValue(ClaimTypes.Email);
        return info?.LoginProvider == "Google" && info.ProviderKey.Length is > 0 and <= 255 && email is { Length: <= 254 } && new EmailAddressAttribute().IsValid(email) && GoogleEmailIsAuthoritative(info.Principal) ? info : null;
    }

    [NonAction]
    public static bool GoogleEmailIsAuthoritative(ClaimsPrincipal principal) =>
        string.Equals(principal.FindFirstValue("google:email_verified"), "true", StringComparison.OrdinalIgnoreCase);

    private async Task<IActionResult> ExistingGoogleEmailAsync(string? returnUrl)
    {
        await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
        TempData["AccountMessage"] = "Use the existing sign-in method for this email address. Google accounts are not linked automatically.";
        return RedirectToAction(nameof(SignIn), new { returnUrl = SafeReturnUrl(returnUrl) });
    }

    private async Task<IActionResult> FinishGoogleAsync(AccountUser user, string? returnUrl)
    {
        if (!await signIn.CanSignInAsync(user) || await users.IsLockedOutAsync(user)) { return await GoogleFailed(); }
        if (!user.EmailConfirmed)
        {
            var info = await GoogleInfoAsync();
            if (info is null || !string.Equals(info.Principal.FindFirstValue(ClaimTypes.Email), user.Email, StringComparison.OrdinalIgnoreCase)) { return await GoogleFailed(); }
            user.EmailConfirmed = true;
            if (!(await users.UpdateAsync(user)).Succeeded) { return await GoogleFailed(); }
        }
        await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
        await signIn.SignInAsync(user, false, "Google");
        TempData["PageArrival"] = true;
        return LocalRedirect(SafeReturnUrl(returnUrl) ?? "/");
    }
}
