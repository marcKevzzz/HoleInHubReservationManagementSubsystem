using HoleInHub.Business.Accounts;
using HoleInHub.Web.Accounts;
using HoleInHub.Web.ViewModels;
using HoleInHubReservationManagementSubsystem.ViewModels;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace HoleInHubReservationManagementSubsystem.Controllers;

[Authorize(Policy = StaffAuthorization.Workspace), AutoValidateAntiforgeryToken]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class StaffProfileController(UserManager<AccountUser> users, SignInManager<AccountUser> signIn, StaffPinAccess access) : Controller
{
    [HttpGet("/staff/profile")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var user = await users.GetUserAsync(User);
        return user is null ? Challenge(StaffPinAccess.Scheme) : View(await ModelAsync(user));
    }

    [HttpPost("/staff/profile")]
    public async Task<IActionResult> Save(AccountContactModel model, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var user = await users.GetUserAsync(User);
        if (user is null) { return Challenge(StaffPinAccess.Scheme); }
        if (!ModelState.IsValid) { return View("Index", await ModelAsync(user, model)); }
        user.FirstName = model.FirstName.Trim();
        user.LastName = model.LastName.Trim();
        user.Phone = ContactRules.PhoneDigits(model.Phone);
        if (!(await users.UpdateAsync(user)).Succeeded)
        {
            ModelState.AddModelError("", "Your profile could not be saved. Reload the page and try again.");
            return View("Index", await ModelAsync(user, model));
        }
        new ToastNotice("success", "Profile updated", "Your details have been saved.").Store(TempData);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("/staff/profile/password"), EnableRateLimiting("accounts")]
    public async Task<IActionResult> Password(StaffPasswordModel model, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var user = await users.GetUserAsync(User);
        if (user is null) { return Challenge(StaffPinAccess.Scheme); }
        if (!ModelState.IsValid) { return View("Index", await ModelAsync(user)); }
        if (!(await signIn.CheckPasswordSignInAsync(user, model.CurrentPassword, lockoutOnFailure: true)).Succeeded)
        {
            ModelState.AddModelError(nameof(model.CurrentPassword), "Unable to verify your current password. Try again later if your account is locked.");
            return View("Index", await ModelAsync(user));
        }
        var result = await users.ChangePasswordAsync(user, model.CurrentPassword, model.Password);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(nameof(model.Password), "Your password could not be changed. Reload the page and try again.");
            return View("Index", await ModelAsync(user));
        }
        await HttpContext.SignOutAsync(StaffPinAccess.Scheme);
        await signIn.SignOutAsync();
        var admin = user.Roles.Contains(StaffRoles.Admin);
        new ToastNotice("success", "Password changed", admin ? "Sign in with your new password." : "Set a new PIN with your updated password to reopen your workspace.").Store(TempData);
        return RedirectToAction(admin ? "SignIn" : "Setup", admin ? "Account" : "StaffAccess");
    }

    private async Task<StaffProfileModel> ModelAsync(AccountUser user, AccountContactModel? contact = null) => new()
    {
        Contact = contact ?? new() { FirstName = user.FirstName, LastName = user.LastName, Phone = ContactRules.PhoneDigits(user.Phone) },
        Email = user.Email,
        Role = user.Roles.Contains(StaffRoles.Admin) ? "Admin" : user.Roles.Contains(StaffRoles.Manager) ? "Manager" : "Staff",
        HasPassword = await users.HasPasswordAsync(user),
        CanSetPin = access.Enabled && !access.IsPresentation && StaffPinAccess.GetPinRoles(user).Any()
    };
}
