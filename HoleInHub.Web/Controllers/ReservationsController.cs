using System.Security.Claims;
using HoleInHub.Business.Reservations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace HoleInHubReservationManagementSubsystem.Controllers;

public sealed class ReservationsController(ReservationService reservations) : Controller
{
    [AllowAnonymous] public async Task<IActionResult> Availability(DateOnly? date, TimeOnly? startTime, int durationMinutes = 60, string service = "Regular", CancellationToken ct = default) { var day = date ?? DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, ReservationRules.Manila).DateTime.AddDays(1)); ViewData["Date"] = day; ViewData["StartTime"] = startTime ?? new TimeOnly(11, 0); ViewData["Duration"] = durationMinutes; ViewData["Service"] = service; try { return View(await reservations.GetAvailabilityAsync(day, startTime ?? new TimeOnly(11, 0), durationMinutes, service, ct)); } catch (ReservationValidationException e) { ModelState.AddModelError("", e.Message); return View(Array.Empty<AvailabilityDto>()); } }
    [AllowAnonymous, HttpGet] public async Task<IActionResult> CheckAvailability(DateOnly date, TimeOnly startTime, int durationMinutes, string service, CancellationToken ct) { try { return Json(await reservations.GetAvailabilityAsync(date, startTime, durationMinutes, service, ct)); } catch (ReservationValidationException error) { return BadRequest(new { message = error.Message }); } }
    [Authorize] public async Task<IActionResult> Index(CancellationToken ct) => View(await reservations.GetOwnedAsync(User.FindFirstValue(ClaimTypes.NameIdentifier)!, ct));
    [Authorize] public async Task<IActionResult> Details(string id, CancellationToken ct) { var booking = await reservations.GetOwnedAsync(User.FindFirstValue(ClaimTypes.NameIdentifier)!, id, ct); return booking is null ? NotFound() : View(booking); }
    [AllowAnonymous]
    public async Task<IActionResult> Create(DateOnly? date, TimeOnly? startTime, int? durationMinutes, string? service, string? resourceId, CancellationToken ct)
    {
        var resources = await reservations.GetCatalogAsync(ct);
        ViewData["Resources"] = resources;
        var model = new SubmitReservationRequest
        {
            Date = date ?? DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, ReservationRules.Manila).DateTime.AddDays(1)),
            StartTime = startTime ?? new TimeOnly(11, 0),
            DurationMinutes = durationMinutes ?? 60,
            Service = service is "Regular" or "VIP" ? service : "Regular",
            IdempotencyKey = Guid.NewGuid().ToString("N")
        };
        try { ReservationRules.Interval(model.Date, model.StartTime, model.DurationMinutes); }
        catch (ReservationValidationException error) { ModelState.AddModelError("", error.Message); }
        if (resourceId is not null && resources.Any(resource => resource.Id == resourceId && resource.Service == model.Service))
        {
            model.ResourceIds = [resourceId];
        }
        return View(model);
    }
    [HttpPost, Authorize, ValidateAntiForgeryToken] public async Task<IActionResult> Create(SubmitReservationRequest request, CancellationToken ct) { if (!User.HasClaim("email_verified", "true")) { ModelState.AddModelError("", "Verify your email before submitting a reservation."); } if (ModelState.IsValid) { try { var result = await reservations.SubmitAsync(request, User.FindFirstValue(ClaimTypes.NameIdentifier)!, User.FindFirstValue(ClaimTypes.Email) ?? "", ct); return RedirectToAction(nameof(Details), new { id = result.Id }); } catch (ReservationValidationException e) { ModelState.AddModelError("", e.Message); } } ViewData["Resources"] = await reservations.GetCatalogAsync(ct); return View(request); }
}
