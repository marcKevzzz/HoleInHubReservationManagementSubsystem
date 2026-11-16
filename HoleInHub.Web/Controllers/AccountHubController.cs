using System.Security.Claims;
using HoleInHub.Business.Accounts;
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
    [HttpGet("/notifications")]
    public async Task<IActionResult> Notifications(int page, CancellationToken ct)
    {
        if (StaffRoles.Workspace.Any(User.IsInRole)) { return Forbid(); }
        return View(await reservations.GetCustomerNotificationsAsync(User.FindFirstValue(ClaimTypes.NameIdentifier)!, page, ct));
    }

    [HttpGet("/notifications/unread")]
    public async Task<IActionResult> NotificationUnread(CancellationToken ct)
    {
        if (StaffRoles.Workspace.Any(User.IsInRole)) { return Forbid(); }
        var unread = await reservations.GetCustomerNotificationBadgeAsync(User.FindFirstValue(ClaimTypes.NameIdentifier)!, ct);
        return unread.HasValue ? Json(new { unread = unread.Value }) : StatusCode(StatusCodes.Status503ServiceUnavailable);
    }

    [HttpPost("/notifications/read-visible"), ValidateAntiForgeryToken, RequestSizeLimit(32768)]
    public async Task<IActionResult> MarkNotificationsRead([FromForm] NotificationReadRow[] rows, CancellationToken ct)
    {
        if (StaffRoles.Workspace.Any(User.IsInRole)) { return Forbid(); }
        if (!ModelState.IsValid || rows.Length is 0 or > ReservationService.CustomerNotificationPageSize) { return BadRequest(); }
        var unread = await reservations.MarkCustomerNotificationsReadAsync(User.FindFirstValue(ClaimTypes.NameIdentifier)!, rows, ct);
        return Json(new { unread });
    }

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
        user.Phone = HoleInHub.Business.Accounts.ContactRules.PhoneDigits(model.Phone);
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
