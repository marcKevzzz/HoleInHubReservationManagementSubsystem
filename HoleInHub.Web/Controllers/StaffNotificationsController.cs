using System.Security.Claims;
using HoleInHub.Business.Accounts;
using HoleInHub.Web.Accounts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HoleInHubReservationManagementSubsystem.Controllers;

[Authorize(Policy = StaffAuthorization.Workspace), AutoValidateAntiforgeryToken]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None), Route("staff/notifications")]
public sealed class StaffNotificationsController(NotificationDeliveryService notifications) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(int page, CancellationToken ct)
    {
        var model = await notifications.ListAsync(User.FindFirstValue(ClaimTypes.NameIdentifier)!, null, page, ct, operational: true, compact: User.IsInRole(StaffRoles.Admin));
        ViewData["NotificationUnread"] = model.Unread;
        return View(model);
    }

    [HttpPost("read-visible"), RequestSizeLimit(32768)]
    public async Task<IActionResult> MarkVisibleRead([FromForm] NotificationReadRow[] rows, CancellationToken ct)
    {
        if (!ModelState.IsValid || rows.Length is 0 or > NotificationDeliveryService.PageSize) { return BadRequest(); }
        var unread = await notifications.MarkRowsReadAsync(User.FindFirstValue(ClaimTypes.NameIdentifier)!, rows, ct, operational: true);
        return Json(new { unread });
    }
}
