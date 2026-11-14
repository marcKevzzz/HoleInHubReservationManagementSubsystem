using System.Security.Claims;
using HoleInHub.Business.Accounts;
using HoleInHub.Web.Accounts;
using HoleInHubReservationManagementSubsystem.Localization;
using HoleInHubReservationManagementSubsystem.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HoleInHubReservationManagementSubsystem.Controllers;

[Authorize(Policy = StaffAuthorization.Workspace, Roles = StaffRoles.Admin)]
[AutoValidateAntiforgeryToken, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("admin/notifications")]
public sealed class NotificationDeliveryController(NotificationDeliveryService delivery) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(string? status, int page, CancellationToken ct)
    {
        var model = await delivery.ListAsync(User.FindFirstValue(ClaimTypes.NameIdentifier)!, status, page, ct);
        ViewData["NotificationUnread"] = model.Unread;
        return View(model);
    }

    [HttpPost("read-visible"), RequestSizeLimit(32768)]
    public async Task<IActionResult> MarkVisibleRead([FromForm] NotificationReadRow[] rows, CancellationToken ct)
    {
        if (!ModelState.IsValid || rows.Length is 0 or > NotificationDeliveryService.PageSize) { return BadRequest(); }
        var unread = await delivery.MarkRowsReadAsync(User.FindFirstValue(ClaimTypes.NameIdentifier)!, rows, ct);
        return Json(new { unread });
    }

    [HttpPost("retry")]
    public async Task<IActionResult> Retry(string? id, string? status, int page, CancellationToken ct)
    {
        var queued = await delivery.RetryAsync(id, ct);
        new ToastNotice(queued ? "success" : "warning", CustomerText.T(queued ? "Email queued for retry" : "Email could not be retried"),
            queued ? null : CustomerText.T("It may already be queued, or its booking or verification code is no longer eligible.")).Store(TempData);
        return RedirectToAction(nameof(Index), new { status, page });
    }
}
