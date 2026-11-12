using System.Security.Claims;
using HoleInHub.Business.Accounts;
using HoleInHub.Business.Activity;
using HoleInHub.Business.Reservations;
using HoleInHub.Business.Services;
using HoleInHub.Web.Accounts;
using HoleInHubReservationManagementSubsystem.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace HoleInHubReservationManagementSubsystem.Controllers;

[Authorize(Policy = StaffAuthorization.Workspace)]
[AutoValidateAntiforgeryToken, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class LeadershipController(ReservationService reservations, AccountService accounts,
    IServiceCatalog services, IOptions<ReservationPolicy> policy, TimeProvider clock, StaffPinAccess staffAccess, ActivityService activity, StaffPinService pins) : Controller
{
    private DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), ReservationRules.Manila).DateTime);

    [HttpGet("/staff/dashboard")]
    public async Task<IActionResult> Dashboard(CancellationToken ct)
    {
        ViewData["PendingReviews"] = await reservations.GetPendingReviewCountAsync(ct);
        return View();
    }

    [Authorize(Roles = StaffRoles.VenueManagement), HttpGet("/manager")]
    public IActionResult Manager() => RedirectToAction(nameof(Dashboard));

    [Authorize(Roles = StaffRoles.VenueManagement), HttpGet("/manager/availability")]
    public IActionResult Availability(DateOnly? date)
    {
        var day = date ?? Today;
        if (!ModelState.IsValid || day <= DateOnly.MinValue || day >= DateOnly.MaxValue) { return BadRequest(); }
        return RedirectToAction(nameof(BookingReviewController.Index), "BookingReview", new { date = day.ToString("yyyy-MM-dd") });
    }

    [Authorize(Roles = StaffRoles.VenueManagement), HttpGet("/manager/team")]
    public IActionResult Team(int? page) => RedirectToAction(nameof(People), new { page, scope = "team" });

    [Authorize(Roles = StaffRoles.VenueManagement), HttpGet("/staff/people")]
    public async Task<IActionResult> People(int page, string? scope, CancellationToken ct)
    {
        var isAdmin = User.IsInRole(StaffRoles.Admin);
        ViewData["TeamCreation"] = !staffAccess.IsPresentation;
        var role = StaffRoles.Workspace.Contains(scope) ? scope : null;
        ViewData["SelectedRole"] = role ?? "all";
        ViewData["RoleEditing"] = isAdmin && !staffAccess.IsPresentation;
        return View("Directory", await accounts.GetDirectoryAsync(true, page, ct, role));
    }

    [Authorize(Roles = StaffRoles.VenueManagement), HttpGet("/staff/people/{id}")]
    public async Task<IActionResult> Person(string id, string? scope, int page, CancellationToken ct)
    {
        var account = await accounts.FindByIdAsync(id, ct);
        if (account is null || (!account.Roles.Any(StaffRoles.Workspace.Contains) && !User.IsInRole(StaffRoles.Admin))) { return NotFound(); }
        ViewData["SelectedRole"] = StaffRoles.Workspace.Contains(scope) ? scope : "all";
        ViewData["DirectoryPage"] = Math.Clamp(page, 1, 1000);
        ViewData["RoleEditing"] = User.IsInRole(StaffRoles.Admin) && !staffAccess.IsPresentation
            && account.Id != User.FindFirstValue(ClaimTypes.NameIdentifier);
        ViewData["CanInvite"] = !staffAccess.IsPresentation && account.Roles.Any(role =>
            StaffRoles.CanCreate(User.IsInRole(StaffRoles.Admin) ? StaffRoles.Admin : StaffRoles.Manager, role));
        var pinAccount = !account.Roles.Contains(StaffRoles.Admin) && account.Roles.Any(role => role is StaffRoles.Staff or StaffRoles.Manager);
        ViewData["UsesPin"] = pinAccount;
        var hasPin = pinAccount && await pins.ReadAsync(id, ct) is not null;
        ViewData["HasPin"] = hasPin;
        ViewData["CanResetPin"] = hasPin && User.IsInRole(StaffRoles.Admin) && !staffAccess.IsPresentation
            && account.Id != User.FindFirstValue(ClaimTypes.NameIdentifier);
        var activityQuery = new ActivityQuery { Range = "month", From = Today.AddDays(-29), To = Today, Actor = account.Id };
        var activityPage = await activity.QueryAsync(new(activityQuery.From.Value, activityQuery.To.Value, Actor: account.Id),
            User.IsInRole(StaffRoles.Admin), User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "", ct);
        ViewData["MemberActivity"] = new ActivityListModel(activityQuery, activityPage with { NextCursor = null }, User.IsInRole(StaffRoles.Admin));
        var roles = StaffRoles.Workspace.Intersect(account.Roles).ToArray();
        return View(new StaffRoleEditModel(new AccountDirectoryEntry(account.Id, account.Email,
            $"{account.FirstName} {account.LastName}".Trim(), account.Roles, account.EmailConfirmed),
            new StaffRoleForm { Version = account.Version, Role = roles.Length switch { 0 => StaffRoleForm.NoStaffAccess, 1 => roles[0], _ => "" } }));
    }

    [Authorize(Roles = StaffRoles.Admin), HttpPost("/admin/users/{id}/pin/reset")]
    public async Task<IActionResult> ResetPin(string id, StaffPinResetForm form, CancellationToken ct)
    {
        if (staffAccess.IsPresentation) { return NotFound(); }
        if (!ModelState.IsValid) { return BadRequest("Enter a reason and reload the account before resetting its PIN."); }
        var result = await accounts.ResetStaffPinAsync(new(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "",
            User.FindFirstValue(StaffPinAccess.SecurityStampClaim) ?? "", id, form.Version, form.CommandId, form.Reason), ct);
        if (result == StaffRoleChangeResult.Forbidden) { return Forbid(StaffPinAccess.Scheme); }
        if (result == StaffRoleChangeResult.NotFound) { return NotFound(); }
        new ToastNotice(result == StaffRoleChangeResult.Applied ? "success" : "warning",
            result == StaffRoleChangeResult.Applied ? "PIN reset" : "PIN could not be reset",
            result == StaffRoleChangeResult.Applied ? "The team member must set up a new PIN." : "The account changed. Review the latest details and try again.").Store(TempData);
        return RedirectToAction(nameof(Person), new { id });
    }

    [Authorize(Roles = StaffRoles.VenueManagement), HttpGet("/manager/reports")]
    public async Task<IActionResult> Reports(CancellationToken ct)
    {
        var day = Today;
        ViewData["Day"] = day;
        return View(await reservations.GetStaffScheduleAsync(day, ct));
    }

    [Authorize(Roles = StaffRoles.VenueManagement), HttpGet("/manager/activity")]
    public IActionResult Activity() => RedirectToAction("Index", "Activity");

    [Authorize(Roles = StaffRoles.VenueManagement), HttpGet("/manager/notifications")]
    public IActionResult Notifications() => RedirectToAction(nameof(Settings));

    [Authorize(Roles = StaffRoles.Admin), HttpGet("/admin")]
    public IActionResult Admin() => RedirectToAction(nameof(Dashboard));

    [Authorize(Roles = StaffRoles.Admin), HttpGet("/admin/activity")]
    public IActionResult AdminActivity() => RedirectToAction("Index", "Activity", new { category = "access" });

    [Authorize(Roles = StaffRoles.Admin), HttpGet("/admin/users")]
    public IActionResult Users(int? page) => RedirectToAction(nameof(People), new { page });

    [Authorize(Roles = StaffRoles.Admin), HttpGet("/admin/users/{id}/role")]
    public async Task<IActionResult> EditUserRole([FromRoute] string id, CancellationToken ct)
    {
        if (staffAccess.IsPresentation) { return NotFound(); }
        if (id == User.FindFirstValue(ClaimTypes.NameIdentifier)) { return Forbid(StaffPinAccess.Scheme); }
        var target = await accounts.FindByIdAsync(id, ct);
        if (target is null) { return NotFound(); }

        var roles = StaffRoles.Workspace.Intersect(target.Roles).ToArray();
        return RoleView(target, new StaffRoleForm
        {
            Version = target.Version,
            Role = roles.Length switch { 0 => StaffRoleForm.NoStaffAccess, 1 => roles[0], _ => "" }
        });
    }

    [Authorize(Roles = StaffRoles.Admin), HttpPost("/admin/users/{id}/role")]
    public async Task<IActionResult> EditUserRole([FromRoute] string id, [FromForm] StaffRoleForm form, CancellationToken ct, string? scope = null, int page = 1)
    {
        if (staffAccess.IsPresentation) { return NotFound(); }
        var actor = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var stamp = User.FindFirstValue(StaffPinAccess.SecurityStampClaim);
        if (actor is null || stamp is null || id == actor) { return Forbid(StaffPinAccess.Scheme); }

        if (ModelState.IsValid)
        {
            var role = form.Role == StaffRoleForm.NoStaffAccess ? null : form.Role;
            var result = await accounts.ChangeStaffRoleAsync(
                new StaffRoleChange(actor, stamp, id, form.Version, role, form.Reason, Guid.NewGuid().ToString("N")), ct);
            switch (result)
            {
                case StaffRoleChangeResult.Applied:
                case StaffRoleChangeResult.Unchanged:
                    var toast = (result, role) switch
                    {
                        (StaffRoleChangeResult.Applied, null) => new ToastNotice("success", "Staff access removed"),
                        (StaffRoleChangeResult.Applied, _) => new ToastNotice("success", "Staff access updated", $"New role: {role}."),
                        (_, null) => new ToastNotice("info", "Staff access unchanged", "This account already has no staff access."),
                        _ => new ToastNotice("info", "Staff access unchanged", $"Already set to {role}.")
                    };
                    toast.Store(TempData);
                    return RedirectToAction(nameof(Person), new { id, scope, page = Math.Clamp(page, 1, 1000) });
                case StaffRoleChangeResult.NotFound:
                    return NotFound();
                case StaffRoleChangeResult.Forbidden:
                    return Forbid(StaffPinAccess.Scheme);
                case StaffRoleChangeResult.Stale:
                    Response.StatusCode = StatusCodes.Status409Conflict;
                    break;
                case StaffRoleChangeResult.Unavailable:
                    Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                    ModelState.AddModelError("", "Role changes are unavailable until the database update is applied.");
                    break;
                case StaffRoleChangeResult.Invalid:
                    ModelState.AddModelError("", "Choose a supported role and enter a reason. Staff access requires a verified email and a password.");
                    break;
            }
        }

        var target = await accounts.FindByIdAsync(id, ct);
        return target is null ? NotFound() : RoleView(target, form);
    }

    private ViewResult RoleView(AccountDetails target, StaffRoleForm form)
    {
        ViewData["RoleEditing"] = User.IsInRole(StaffRoles.Admin) && !staffAccess.IsPresentation
            && target.Id != User.FindFirstValue(ClaimTypes.NameIdentifier);
        var scope = Request.Query["scope"].ToString();
        ViewData["SelectedRole"] = StaffRoles.Workspace.Contains(scope) ? scope : "all";
        ViewData["DirectoryPage"] = int.TryParse(Request.Query["page"], out var page) ? Math.Clamp(page, 1, 1000) : 1;
        return View(nameof(Person), new StaffRoleEditModel(
            new AccountDirectoryEntry(target.Id, target.Email, $"{target.FirstName} {target.LastName}".Trim(), target.Roles, target.EmailConfirmed),
            form, Response.StatusCode == StatusCodes.Status409Conflict));
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
    public IActionResult AdminSettings() => RedirectToAction(nameof(Settings));

    [Authorize(Roles = StaffRoles.VenueManagement), HttpGet("/staff/settings")]
    public IActionResult Settings() => View(policy.Value);
}
