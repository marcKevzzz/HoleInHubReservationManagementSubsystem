using HoleInHub.Business.Reservations;
using HoleInHub.Web.Accounts;
using HoleInHub.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace HoleInHubReservationManagementSubsystem.Controllers;

[Authorize, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AccountHubController(UserManager<AccountUser> users, ReservationService reservations) : Controller
{
    [HttpGet("/transactions")]
    public async Task<IActionResult> Transactions(CancellationToken ct)
    {
        var user = await users.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        await reservations.ProcessPendingAsync(ct);
        var records = await reservations.GetOwnedAsync(user.Id, ct);
        return View(records.Where(x => x.Service == "WholeShop" && x.PaymentStatus != "NotRequired").OrderByDescending(x => x.CreatedUtc).ToArray());
    }

    [HttpGet("/my-profile")]
    public async Task<IActionResult> Profile(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var user = await users.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        ViewData["Email"] = user.Email;
        return View(new AccountContactModel { FirstName = user.FirstName, LastName = user.LastName, Phone = HoleInHub.Business.Accounts.ContactRules.PhoneDigits(user.Phone) });
    }

    [HttpPost("/my-profile"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Profile(AccountContactModel model, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var user = await users.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        ViewData["Email"] = user.Email;
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        user.FirstName = model.FirstName.Trim();
        user.LastName = model.LastName.Trim();
        user.Phone = model.Phone.Trim();
        var result = await users.UpdateAsync(user);
        if (!result.Succeeded)
        {
            ModelState.AddModelError("", "Your profile could not be saved. Reload the page and try again.");
            return View(model);
        }
        TempData["ProfileSaved"] = "Your profile has been updated.";
        return RedirectToAction(nameof(Profile));
    }
}
