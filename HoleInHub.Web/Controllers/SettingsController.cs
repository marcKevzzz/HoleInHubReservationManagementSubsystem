using System.Security.Claims;
using HoleInHub.Business.Accounts;
using HoleInHub.Business.Services;
using HoleInHub.Business.Settings;
using HoleInHub.Web.Accounts;
using HoleInHubReservationManagementSubsystem.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace HoleInHubReservationManagementSubsystem.Controllers;

[Authorize(Policy = StaffAuthorization.Workspace, Roles = StaffRoles.VenueManagement)]
[AutoValidateAntiforgeryToken, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class SettingsController(OperationalSettings settings) : Controller
{
    [HttpGet("/staff/settings")]
    public async Task<IActionResult> Index(CancellationToken ct) => View(SettingsForm.From(await settings.GetAsync(ct)));
    [Authorize(Roles = StaffRoles.Admin), HttpGet("/staff/gallery-content")]
    public IActionResult GalleryContent() => View();

    [HttpGet("/staff/settings/health")]
    public async Task<IActionResult> Health([FromServices] HoleInHub.Business.Services.IHealthService health, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        try
        {
            var (ready, _) = await health.CheckDatabaseReadinessAsync(timeout.Token);
            return Json(new { ready, checkedUtc = DateTime.UtcNow });
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            return Json(new { ready = false, checkedUtc = DateTime.UtcNow });
        }
    }
    [Authorize(Roles = StaffRoles.Admin), HttpPost("/staff/settings/save"), RequestSizeLimit(100000)]
    public async Task<IActionResult> Save(SettingsForm form, CancellationToken ct)
    {
        if (ModelState.IsValid)
        {
            try
            {
                await settings.SaveAsync(form.Command(), User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "", ct);
                new ToastNotice("success", "Settings saved", "New requests use the updated review policy.").Store(TempData);
                return RedirectToAction(nameof(Index));
            }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException)
            { ModelState.AddModelError("", error.Message); }
        }
        new ToastNotice("error", "Settings not saved", "Review the highlighted fields.").Store(TempData);
        return View("Index", form);
    }
}
[AllowAnonymous, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class VenuePoliciesController(OperationalSettings settings, ILogger<VenuePoliciesController> logger) : Controller
{
    [HttpGet("/policies")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        try
        {
            var current = await settings.GetAsync(ct);
            return View("~/Views/Settings/Policies.cshtml", current.PoliciesPublished ? current : null);
        }
        catch (Exception error) when (!ct.IsCancellationRequested && ServiceAvailability.Translate(error) is not null)
        {
            logger.LogWarning("Venue settings are unavailable; rendering the local public policy copy.");
            return View("~/Views/Settings/Policies.cshtml", model: null);
        }
    }
}
