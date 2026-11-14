using HoleInHub.Business.Reservations;
using HoleInHub.Web.Accounts;
using HoleInHubReservationManagementSubsystem.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace HoleInHubReservationManagementSubsystem.Controllers;

public sealed partial class LeadershipController
{
    [HttpGet("/staff/dashboard")]
    public async Task<IActionResult> Dashboard([FromQuery] DashboardQuery query, CancellationToken ct)
    {
        if (query.Mode == "trends" && !User.ManagesVenue())
        {
            return Forbid(StaffPinAccess.Scheme);
        }

        if (!ModelState.IsValid)
        {
            return BadRequest("Choose valid dashboard dates.");
        }

        try
        {
            var (from, to) = query.Resolve(Today);
            var data = await reservations.GetDashboardAsync(from, to, query.Mode == "today" && from == Today, ct);
            ViewData["PendingReviews"] = data.Attention.GetValueOrDefault("awaiting");
            var model = new DashboardViewModel(data, query.Mode, User.ManagesVenue())
            {
                LatestActivity = await activity.LatestAsync(User.IsInRole(HoleInHub.Business.Accounts.StaffRoles.Admin), ct)
            };
            return Request.Headers["X-Dashboard"] == "true" ? PartialView("_DashboardContent", model) : View(model);
        }
        catch (ReservationValidationException error) { return BadRequest(error.Message); }
    }

    [HttpGet("/staff/dashboard/bookings")]
    public async Task<IActionResult> DashboardBookings([FromQuery] DashboardQuery query, string kind = "all", string? value = null,
        string? group = null, int page = 1, CancellationToken ct = default)
    {
        if (query.Mode == "trends" && !User.ManagesVenue())
        {
            return Forbid(StaffPinAccess.Scheme);
        }

        if (!ModelState.IsValid)
        {
            return BadRequest("Choose valid dashboard dates.");
        }

        try
        {
            var (from, to) = query.Resolve(Today);
            var slice = await reservations.GetDashboardSliceAsync(from, to, kind, value, group, page, ct);
            // The drill-down needs only filter context, never a second aggregate query.
            var context = new DashboardViewModel(new(from, to, clock.GetUtcNow().UtcDateTime, 0, 0, 0, 0, [], [], [], [], new Dictionary<string, long>(), []), query.Mode, User.ManagesVenue());
            var model = new DashboardBookingsViewModel(context, slice, kind, value, group, page);
            ViewData["LeadershipPage"] = "Dashboard";
            return Request.Headers["X-Dashboard-Slice"] == "true" ? PartialView("_DashboardBookings", model) : View("DashboardBookings", model);
        }
        catch (ReservationValidationException error) { return BadRequest(error.Message); }
    }
}
