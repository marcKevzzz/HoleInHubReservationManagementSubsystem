using System.Security.Claims;
using HoleInHub.Business.Reservations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HoleInHubReservationManagementSubsystem.Controllers;

[Authorize(Roles = "Staff,Admin"), ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class BookingReviewController(ReservationService reservations) : Controller
{
    [HttpGet] public async Task<IActionResult> Index(CancellationToken ct) => View(await reservations.GetReviewQueueAsync(ct));
    [HttpGet]
    public async Task<IActionResult> Details(string id, CancellationToken ct)
    {
        var booking = await reservations.GetStaffAsync(id, ct);
        return booking == null ? NotFound() : View(booking);
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Decide(string id, string decision, string? reason, CancellationToken ct)
    {
        if (decision is not ("approve" or "reject" or "coach" or "cancel" or "keep"))
        {
            return BadRequest();
        }

        try
        {
            var changed = await reservations.ReviewAsync(id, decision, User.FindFirstValue(ClaimTypes.NameIdentifier)!, ct, reason);
            TempData["BookingMessage"] = changed ? "Decision recorded." : "This action is no longer available. Refresh the booking status and check its approvals/deadline.";
        }
        catch (ReservationValidationException error) { TempData["BookingMessage"] = error.Message; }
        return RedirectToAction(nameof(Details), new { id });
    }
}
