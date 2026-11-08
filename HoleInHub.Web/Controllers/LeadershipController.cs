using HoleInHub.Business.Accounts;
using HoleInHub.Business.Reservations;
using HoleInHub.Business.Services;
using HoleInHub.Web.Accounts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace HoleInHubReservationManagementSubsystem.Controllers;

[Authorize(Policy = StaffAuthorization.Workspace, Roles = StaffRoles.VenueManagement)]
[AutoValidateAntiforgeryToken, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class LeadershipController(ReservationService reservations, AccountService accounts,
    IServiceCatalog services, IOptions<ReservationPolicy> policy, TimeProvider clock) : Controller
{
    private DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), ReservationRules.Manila).DateTime);

    [HttpGet("/manager")]
    public async Task<IActionResult> Manager(CancellationToken ct)
    {
        ViewData["PendingReviews"] = await reservations.GetPendingReviewCountAsync(ct);
        return View();
    }

    [HttpGet("/manager/availability")]
    public async Task<IActionResult> Availability(DateOnly? date, CancellationToken ct)
    {
        var day = date ?? Today;
        if (!ModelState.IsValid || day <= DateOnly.MinValue || day >= DateOnly.MaxValue) { return BadRequest(); }
        ViewData["Day"] = day;
        return View(await reservations.GetStaffScheduleAsync(day, ct));
    }

    [HttpGet("/manager/team")]
    public async Task<IActionResult> Team(int page, CancellationToken ct)
    {
        ViewData["Title"] = "Team";
        return View("Directory", await accounts.GetDirectoryAsync(true, page, ct));
    }

    [HttpGet("/manager/reports")]
    public async Task<IActionResult> Reports(CancellationToken ct)
    {
        var day = Today;
        ViewData["Day"] = day;
        return View(await reservations.GetStaffScheduleAsync(day, ct));
    }

    [HttpGet("/manager/activity")]
    public async Task<IActionResult> Activity(CancellationToken ct) => View(await reservations.GetStaffScheduleAsync(Today, ct));

    [HttpGet("/manager/notifications")]
    public IActionResult Notifications() => View(policy.Value);

    [Authorize(Roles = StaffRoles.Admin), HttpGet("/admin")]
    public IActionResult Admin() => View();

    [Authorize(Roles = StaffRoles.Admin), HttpGet("/admin/users")]
    public async Task<IActionResult> Users(int page, CancellationToken ct)
    {
        ViewData["Title"] = "Users & roles";
        return View("Directory", await accounts.GetDirectoryAsync(false, page, ct));
    }

    [Authorize(Roles = StaffRoles.Admin), HttpGet("/admin/resources")]
    public async Task<IActionResult> Resources(CancellationToken ct) => View(await reservations.GetResourceDirectoryAsync(ct));

    [Authorize(Roles = StaffRoles.Admin), HttpPost("/admin/resources")]
    public async Task<IActionResult> AddBay(string? id, string? name, string? service, CancellationToken ct)
    {
        try
        {
            await reservations.AddBayAsync(id ?? "", name ?? "", service ?? "", ct);
            TempData["ResourceMessage"] = "Bay added.";
            return RedirectToAction(nameof(Resources));
        }
        catch (ReservationValidationException ex)
        {
            ModelState.AddModelError("", ex.Message);
            return View(nameof(Resources), await reservations.GetResourceDirectoryAsync(ct));
        }
    }

    [Authorize(Roles = StaffRoles.Admin), HttpPost("/admin/resources/active")]
    public async Task<IActionResult> SetBayActive(string? id, bool? active, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 80 || active is null) { return BadRequest(); }
        try
        {
            if (!await reservations.SetBayActiveAsync(id, active.Value, ct)) { return NotFound(); }
            TempData["ResourceMessage"] = active.Value ? "Bay restored to inventory." : "Bay retired from inventory.";
            return RedirectToAction(nameof(Resources));
        }
        catch (ReservationValidationException ex)
        {
            ModelState.AddModelError("", ex.Message);
            return View(nameof(Resources), await reservations.GetResourceDirectoryAsync(ct));
        }
    }

    [Authorize(Roles = StaffRoles.Admin), HttpGet("/admin/services")]
    public async Task<IActionResult> Services(CancellationToken ct) => View(await services.GetActiveAsync(ct));

    [Authorize(Roles = StaffRoles.Admin), HttpGet("/admin/settings")]
    public IActionResult Settings() => View(policy.Value);
}
