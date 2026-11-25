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
    public async Task<IActionResult> Notifications(int page, CancellationToken ct, string? filter = null)
    {
        if (StaffRoles.Workspace.Any(User.IsInRole)) { return Forbid(); }
        ViewData["NotificationFilter"] = filter == "unread" ? "unread" : "all";
        return View(await reservations.GetCustomerNotificationsAsync(User.FindFirstValue(ClaimTypes.NameIdentifier)!, page, ct, filter == "unread"));
    }

    [HttpPost("/notifications/action"), ValidateAntiForgeryToken, RequestSizeLimit(32768)]
    public async Task<IActionResult> NotificationAction(string operation, string? id, long activityTicks, string? filter, CancellationToken ct)
    {
        if (StaffRoles.Workspace.Any(User.IsInRole)) { return Forbid(); }
        if (!ModelState.IsValid) { return BadRequest(); }
        var accountId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        if (operation == "read-all") { await reservations.MarkAllCustomerNotificationsReadAsync(accountId, ct); }
        else if (!string.IsNullOrEmpty(id) && activityTicks > 0 && operation is "read" or "delete")
        {
            var row = new NotificationReadRow(id, activityTicks);
            if (operation == "read") { await reservations.MarkCustomerNotificationsReadAsync(accountId, [row], ct); }
            else if (!await reservations.DismissCustomerNotificationAsync(accountId, row, ct)) { return BadRequest(); }
        }
        else { return BadRequest(); }
        return RedirectToAction(nameof(Notifications), new { filter = filter == "unread" ? "unread" : "all" });
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
        var records = await reservations.GetOwnedAsync(User.FindFirstValue(ClaimTypes.NameIdentifier)!, ct);
        return View(records.Where(x => x.Service == "WholeShop" && x.PaymentStatus != "NotRequired").OrderByDescending(x => x.CreatedUtc).ToArray());
    }

    [HttpGet("/my-profile")]
    public async Task<IActionResult> Profile(CancellationToken ct, bool edit = false)
    {
        ct.ThrowIfCancellationRequested();
        var user = await users.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        ViewData["ProfileEditing"] = edit;
        ViewData["Email"] = user.Email;
        return View(new AccountProfileModel { FirstName = user.FirstName, LastName = user.LastName, Phone = HoleInHub.Business.Accounts.ContactRules.PhoneDigits(user.Phone), Age = user.Age, Address = user.Address });
    }

    [HttpPost("/my-profile"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Profile(AccountProfileModel model, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var user = await users.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        ViewData["Email"] = user.Email;
        if (!string.IsNullOrEmpty(model.PhoneCountry) && !HoleInHub.Business.Accounts.ContactRules.IsCountryPhone(model.PhoneCountry, model.Phone))
        {
            ModelState.AddModelError(nameof(model.Phone), "Check the contact number's length and selected country code.");
        }
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        user.Age = model.Age;
        user.Address = string.IsNullOrWhiteSpace(model.Address) ? null : model.Address.Trim();
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
