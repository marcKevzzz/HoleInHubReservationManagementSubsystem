using HoleInHub.Business.Services;
using HoleInHub.Business.Accounts;
using HoleInHub.Web.Accounts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;

namespace HoleInHubReservationManagementSubsystem.Controllers;

[Authorize(Policy = StaffAuthorization.Workspace, Roles = StaffRoles.Admin)]
[AutoValidateAntiforgeryToken, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("admin/api-access")]
public sealed class ApiAccessController(IntegrationAccess access, StaffPinAccess pins) : Controller
{
    [HttpGet("keys")]
    public async Task<IActionResult> Keys(CancellationToken ct) => View(await access.KeysAsync(ct));
    [HttpGet("logs")]
    public async Task<IActionResult> Logs(string? key, CancellationToken ct) => View(await access.LogsAsync(key,ct));
    [HttpGet("keys/new")]
    public IActionResult Create() => View();
    [HttpPost("keys/new")]
    public async Task<IActionResult> Create(string name, int days, string[] permission, CancellationToken ct)
    {
        if (pins.IsPresentation) { return Forbid(); }
        if (string.IsNullOrWhiteSpace(name) || name.Length > 80 || days is not (30 or 60 or 90) || !IntegrationAccess.ValidPermissions(permission))
        {
            ModelState.AddModelError("", "Enter a name, expiration and permission.");
            return View();
        }
        return View("Issued", await access.IssueAsync(name, days, permission, ct));
    }
    [HttpPost("keys/{id}/revoke")]
    public async Task<IActionResult> Revoke(string id,CancellationToken ct)
    {
        if (pins.IsPresentation) { return Forbid(); }
        await access.RevokeAsync(id,ct);
        return RedirectToAction(nameof(Keys));
    }
}

[ApiController, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class IntegrationRatesController(IntegrationAccess access, IServiceCatalog catalog) : ControllerBase
{
    [HttpGet("/api/v1/service-rates")]
    public Task<IActionResult> Rates(CancellationToken ct) => ReadAsync(IntegrationAccess.ReadRates, "GET /api/v1/service-rates", ct);

    [HttpGet("/api/v1/services")]
    public Task<IActionResult> Services(CancellationToken ct) => ReadAsync(IntegrationAccess.ReadServices, "GET /api/v1/services", ct);

    private async Task<IActionResult> ReadAsync(string permission, string route, CancellationToken ct)
    {
        var start = Stopwatch.GetTimestamp();
        var (status, caller) = await access.CheckAsync(Request.Headers["X-Api-Key"].ToString(), permission, ct);
        try
        {
            if (status != 200) { return StatusCode(status); }
            var services = await catalog.GetActiveAsync(ct);
            if (permission == IntegrationAccess.ReadServices)
            {
                return Ok(services.Select(s => new { s.Id, s.Title, s.Summary, s.BookingService, s.MaxPax }));
            }
            return Ok(services.Select(s => new { s.Id, s.Title, s.RateText }));
        }
        catch { status = 500; throw; }
        finally
        {
            await access.LogAsync(new(HttpContext.TraceIdentifier, caller, route, status, DateTime.UtcNow, (long)Stopwatch.GetElapsedTime(start).TotalMilliseconds),ct);
        }
    }
}



