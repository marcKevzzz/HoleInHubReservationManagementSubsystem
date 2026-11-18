using HoleInHub.Web.Accounts;
using HoleInHub.Web.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace HoleInHub.Web.Controllers;

public sealed partial class AccountController
{
    [HttpGet("activate")]
    public IActionResult Activate(string? userId, string? token) => View(new ResetPasswordModel { UserId = userId ?? "", Token = token ?? "" });

    [HttpPost("activate")]
    public async Task<IActionResult> Activate(ResetPasswordModel model, CancellationToken ct)
    {
        ModelState.Remove("ReturnUrl");
        if (!ModelState.IsValid) { return View(model); }
        ct.ThrowIfCancellationRequested();
        var user = await users.FindByIdAsync(model.UserId);
        var token = Decode(model.Token);
        if (user is { EmailConfirmed: false } && token is not null)
        {
            // The emailed token proves mailbox access; password and verification persist together.
            user.EmailConfirmed = true;
            var result = await users.ResetPasswordAsync(user, token, model.Password);
            if (result.Succeeded)
            {
                TempData["AccountMessage"] = "Account activated. Sign in with your email and password.";
                return RedirectToAction(nameof(SignIn));
            }
            foreach (var error in result.Errors) { ModelState.AddModelError("", error.Description); }
        }
        else { ModelState.AddModelError("", "This activation link is invalid, expired or already used. Contact your administrator."); }
        return View(model);
    }
}
