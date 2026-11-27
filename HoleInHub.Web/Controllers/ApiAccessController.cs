using HoleInHub.Business.Services;
using HoleInHub.Business.Accounts;
using HoleInHub.Web.Accounts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using System.Security.Claims;
using Microsoft.Extensions.Caching.Memory;
using System.ComponentModel.DataAnnotations;
using HoleInHub.Business.Customers;
using HoleInHub.Business.Reservations;
using HoleInHub.Web.ViewModels;
using HoleInHubReservationManagementSubsystem.ViewModels;

namespace HoleInHubReservationManagementSubsystem.Controllers;

[Authorize(Policy = StaffAuthorization.Workspace, Roles = StaffRoles.Admin)]
[AutoValidateAntiforgeryToken, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("admin/api-access")]
public sealed class ApiAccessController(IntegrationAccess access, StaffPinAccess pins, IMemoryCache cache) : Controller
{
    [HttpGet("keys")]
    public async Task<IActionResult> Keys(CancellationToken ct) => View(await access.KeysAsync(ct));
    [HttpGet("logs")]
    public async Task<IActionResult> Logs(string? key, CancellationToken ct) => View(await access.LogsAsync(key,ct));
    [HttpGet("keys/new")]
    public IActionResult Create() => View();
    [HttpPost("keys/new")]
    public async Task<IActionResult> Create(string name, int days, string[] permission, Guid submission, CancellationToken ct)
    {
        if (pins.IsPresentation) { return Forbid(); }
        if (submission == Guid.Empty)
        {
            ModelState.AddModelError("", "Reload the creation form and try again.");
            return View();
        }
        if (string.IsNullOrWhiteSpace(name) || name.Length > 80 || days is not (30 or 60 or 90) || !IntegrationAccess.ValidPermissions(permission))
        {
            ModelState.AddModelError("", "Enter a name, expiration and permission.");
            return View();
        }
        if (User.FindFirstValue(ClaimTypes.NameIdentifier) is not string actor) { return Forbid(); }
        var key = await access.IssueForSubmissionAsync(actor, submission, name, days, permission, ct);
        var receipt = submission;
        if (key is null) { return RedirectToAction(nameof(Issued), new { receipt }); }
        var issued = new IssuedApiKey(name.Trim(), key, days,
            IntegrationAccess.Catalog.Where(scope => permission.Contains(scope.Code)).ToArray());
        cache.Set((actor, receipt), new PendingApiKey(issued), TimeSpan.FromMinutes(1));
        return RedirectToAction(nameof(Issued), new { receipt });
    }

    [HttpGet("keys/issued/{receipt:guid}")]
    public IActionResult Issued(Guid receipt)
    {
        if (User.FindFirstValue(ClaimTypes.NameIdentifier) is not string actor || pins.IsPresentation) { return Forbid(); }
        var cacheKey = (actor, receipt);
        if (cache.Get<PendingApiKey>(cacheKey)?.Take() is not { } issued)
        {
            return RedirectToAction(nameof(Keys));
        }
        cache.Remove(cacheKey);
        return View(issued);
    }
    [HttpPost("keys/{id}/delete")]
    public async Task<IActionResult> Delete(string id, CancellationToken ct)
    {
        if (pins.IsPresentation) { return Forbid(); }
        await access.DeleteAsync(id, ct);
        new ToastNotice("success", "API key deleted").Store(TempData);
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

    private Task<IActionResult> ReadAsync(string permission, string route, CancellationToken ct)
        => WithKeyAsync(permission, route, async _ =>
        {
            var services = await catalog.GetActiveAsync(ct);
            if (permission == IntegrationAccess.ReadServices)
            {
                return Ok(services.Select(s => new { s.Id, s.Title, s.Summary, s.BookingService, s.MaxPax }));
            }
            return Ok(services.Select(s => new { s.Id, s.Title, s.RateText }));
        }, ct);

    [HttpGet("/api/v1/services/{id}/details")]
    public Task<IActionResult> Details(string id, [FromServices] ServiceCatalog services, CancellationToken ct)
        => WithKeyAsync(IntegrationAccess.UpdateServices, "GET /api/v1/services/{id}/details", async _ =>
        {
            var item = await services.GetAsync(id, ct);
            return item is null ? NotFound("Unknown service.") : Ok(new { item.Id, item.Title, item.Summary, item.Details, item.Revision });
        }, ct);

    [HttpGet("/api/v1/services/{id}/publication")]
    public Task<IActionResult> Publication(string id, [FromServices] ServiceCatalog services, CancellationToken ct)
        => WithKeyAsync(IntegrationAccess.PublishServices, "GET /api/v1/services/{id}/publication", async _ =>
        {
            var item = await services.GetAsync(id, ct);
            return item is null ? NotFound("Unknown service.") : Ok(new { item.Id, item.Active, item.Revision });
        }, ct);

    [HttpPost("/api/v1/services")]
    public Task<IActionResult> CreateService(IntegrationServiceDraft request, [FromServices] ServiceCatalog services,
        [FromServices] IWebHostEnvironment environment, CancellationToken ct)
        => WithKeyAsync(IntegrationAccess.CreateServices, "POST /api/v1/services", async caller =>
        {
            if (!Guid.TryParseExact(Request.Headers["Idempotency-Key"].ToString(), "D", out var command))
            { return BadRequest("Supply a UUID in the Idempotency-Key header."); }
            if (!ServiceCatalog.BookingTypes.Contains(request.BookingService)) { return BadRequest("Unknown booking type."); }
            var id = "offer-" + caller + "-" + command.ToString("N");
            var pricing = PricingRules.Default(request.BookingService);
            var draft = new ServiceEdit(id, 0, command.ToString("N"), true, request.Title, request.Summary,
                PricingRules.Summary(request.BookingService, pricing), request.Details, request.BookingService,
                "Book", "Request a booking", 0, false, "None", 0, []) { Pricing = pricing };
            var cover = await System.IO.File.ReadAllBytesAsync(Path.Combine(environment.WebRootPath, "media", "facebook", "venue-overview.webp"), ct);
            try { await services.SaveAsync(draft, [cover], "integration:" + caller, ct); }
            catch (ArgumentException error) { return BadRequest(error.Message); }
            catch (InvalidOperationException error) { return Conflict(error.Message); }
            return Ok(new { id });
        }, ct);

    [HttpGet("/api/v1/services/{id}/rate")]
    public Task<IActionResult> Rate(string id, [FromServices] ServiceCatalog services, CancellationToken ct)
        => WithKeyAsync(IntegrationAccess.UpdateRates, "GET /api/v1/services/{id}/rate", async _ =>
        {
            var item = await services.GetAsync(id, ct);
            return item is null ? NotFound("Unknown service.") : Ok(new { item.Revision,
                baseRate = (item.Pricing ?? PricingRules.Default(item.BookingService)).BaseRate });
        }, ct);

    [HttpPut("/api/v1/services/{id}/rate")]
    public Task<IActionResult> UpdateRate(string id, IntegrationRate request, [FromServices] ServiceCatalog services, CancellationToken ct)
        => ChangeServiceAsync(IntegrationAccess.UpdateRates, "PUT /api/v1/services/{id}/rate", id, services,
            (item, command, actor) =>
            {
                return services.SaveAsync(ServiceCatalog.WithBaseRate(item, request.BaseRate!.Value) with {
                    Revision = request.Revision!.Value, CommandId = command }, [], actor, ct);
            }, ct);

    [HttpPut("/api/v1/services/{id}/details")]
    public Task<IActionResult> UpdateDetails(string id, IntegrationServiceDetails request, [FromServices] ServiceCatalog services, CancellationToken ct)
        => ChangeServiceAsync(IntegrationAccess.UpdateServices, "PUT /api/v1/services/{id}/details", id, services,
            (item, command, actor) => services.SaveAsync(item with { Revision = request.Revision!.Value, CommandId = command,
                Title = request.Title, Summary = request.Summary, Details = request.Details }, [], actor, ct), ct);

    [HttpPut("/api/v1/services/{id}/publication")]
    public Task<IActionResult> UpdatePublication(string id, IntegrationPublication request, [FromServices] ServiceCatalog services, CancellationToken ct)
        => ChangeServiceAsync(IntegrationAccess.PublishServices, "PUT /api/v1/services/{id}/publication", id, services,
            (item, command, actor) => services.SetStateAsync(item.Id, request.Revision!.Value, command,
                request.Active == true ? "activate" : "deactivate", actor, ct), ct);

    private Task<IActionResult> ChangeServiceAsync(string permission, string route, string id, ServiceCatalog services,
        Func<ServiceEdit, string, string, Task> change, CancellationToken ct)
        => WithKeyAsync(permission, route, async caller =>
        {
            if (!Guid.TryParseExact(Request.Headers["Idempotency-Key"].ToString(), "D", out var command))
            {
                return BadRequest("Supply a UUID in the Idempotency-Key header.");
            }
            var item = await services.GetAsync(id, ct);
            if (item is null) { return NotFound("Unknown service."); }
            try { await change(item, command.ToString("N"), "integration:" + caller); }
            catch (ArgumentException error) { return BadRequest(error.Message); }
            catch (InvalidOperationException error) { return Conflict(error.Message); }
            catch (ServiceImpactException) { return Conflict("Upcoming bookings require staff review before this change."); }
            return Ok(new { id });
        }, ct);

    [HttpGet("/api/v1/coach-availability")]
    public Task<IActionResult> CoachAvailability([FromQuery, StringLength(80)] string serviceId,
        [FromQuery] DateOnly date, [FromQuery] TimeOnly startTime, [FromQuery] int durationMinutes,
        [FromServices] ReservationService reservations, CancellationToken ct)
        => WithKeyAsync(IntegrationAccess.ReadCoachAvailability, "GET /api/v1/coach-availability", async _ =>
        {
            try
            {
                var coaches = await reservations.AvailableSessionCoachesAsync(serviceId, date, startTime, durationMinutes, ct);
                return Ok(coaches.Select(coach => new { coach.Id, coach.Name }));
            }
            catch (ReservationValidationException error) { return BadRequest(error.Message); }
        }, ct);

    [HttpGet("/api/v1/bays")]
    public Task<IActionResult> Bays([FromServices] ReservationService reservations, CancellationToken ct)
        => WithKeyAsync(IntegrationAccess.ReadBays, "GET /api/v1/bays", async _ =>
        {
            var bays = await reservations.GetResourceDirectoryAsync(ct);
            return Ok(bays.Select(b => new { b.Id, b.Name, b.Service, b.Active, b.MaxPlayers, b.MaxTotalGuests }));
        }, ct);

    [HttpGet("/api/v1/reservations")]
    public Task<IActionResult> Reservations([FromQuery] DateOnly date, [FromServices] ReservationService reservations,
        CancellationToken ct, [FromQuery] int page = 1)
        => WithKeyAsync(IntegrationAccess.ReadReservations, "GET /api/v1/reservations", async _ =>
        {
            try
            {
                var (rows, total) = await reservations.GetScheduleListAsync(date, date, page, ct);
                return Ok(new { page, total, items = rows.Select(r => new { r.Id, r.Reference, r.Service, r.Status, r.StartUtc, r.EndUtc }) });
            }
            catch (ReservationValidationException error) { return BadRequest(error.Message); }
        }, ct);

    [HttpGet("/api/v1/reservations/{id}")]
    public Task<IActionResult> Reservation(string id, [FromServices] ReservationService reservations, CancellationToken ct)
        => WithKeyAsync(IntegrationAccess.ReadReservations, "GET /api/v1/reservations/{id}", async _ =>
        {
            var row = await reservations.GetStaffAsync(id, ct);
            return row is null ? NotFound("Reservation not found.") : Ok(new { row.Id, row.Reference, row.Service, row.Status, row.StartUtc, row.EndUtc });
        }, ct);

    [HttpGet("/api/v1/reservations/{id}/bays")]
    public Task<IActionResult> ReservedBays(string id, [FromServices] ReservationService reservations, CancellationToken ct)
        => WithKeyAsync(IntegrationAccess.ReadReservedBays, "GET /api/v1/reservations/{id}/bays", async _ =>
        {
            var row = await reservations.GetStaffAsync(id, ct);
            return row is null ? NotFound("Reservation not found.") : Ok(new { row.Id, bayIds = row.ResourceIds });
        }, ct);

    [HttpPost("/api/v1/customers/shared-profile")]
    public Task<IActionResult> SharedProfile([FromServices] CustomerShareCode codes, CancellationToken ct)
        => WithKeyAsync(IntegrationAccess.ReadCustomerProfile, "POST /api/v1/customers/shared-profile", caller =>
        {
            var contact = codes.Redeem(caller, Request.Headers["X-Customer-Approval"].ToString());
            return Task.FromResult<ObjectResult>(contact is null
                ? StatusCode(403, "Ask the customer for a new approval code.") : Ok(contact));
        }, ct);

    [HttpPost("/api/v1/customers")]
    public Task<IActionResult> CreateCustomer([FromBody] IntegrationCustomerContact contact,
        [FromServices] CustomerProfileService profiles, CancellationToken ct)
        => WithKeyAsync(IntegrationAccess.CreateCustomers, "POST /api/v1/customers", async caller =>
        {
            if (!Guid.TryParseExact(Request.Headers["Idempotency-Key"].ToString(), "D", out var commandId))
            {
                return BadRequest("Supply a UUID in the Idempotency-Key header.");
            }
            try
            {
                var id = await profiles.CreateAsync(commandId.ToString("N"), contact.FirstName, contact.LastName,
                    contact.Email ?? "", contact.Phone, "integration:" + caller, ct);
                return Ok(new { customerId = id });
            }
            catch (ReservationValidationException error)
            {
                return BadRequest(error.Message);
            }
        }, ct);

    private async Task<IActionResult> WithKeyAsync(string permission, string route,
        Func<string, Task<ObjectResult>> action, CancellationToken ct)
    {
        var start = Stopwatch.GetTimestamp();
        var (status, caller) = await access.CheckAsync(Request.Headers["X-Api-Key"].ToString(), permission, ct);
        try
        {
            if (status != 200) { return StatusCode(status); }
            var result = await action(caller!);
            status = result.StatusCode ?? 200;
            return result;
        }
        catch { status = 500; throw; }
        finally
        {
            await access.LogAsync(new(HttpContext.TraceIdentifier, caller, route, status, DateTime.UtcNow, (long)Stopwatch.GetElapsedTime(start).TotalMilliseconds),ct);
        }
    }
}

public sealed class IntegrationCustomerContact : AccountContactModel
{
    [EmailAddress, StringLength(254)] public string? Email { get; set; }
}

public sealed record IntegrationServiceDetails([Required, Range(0, long.MaxValue)] long? Revision,
    [Required] string Title, [Required] string Summary, [Required] string Details);
public sealed record IntegrationPublication([Required, Range(0, long.MaxValue)] long? Revision, [Required] bool? Active);
public sealed record IntegrationServiceDraft([Required] string Title, [Required] string Summary,
    [Required] string Details, [Required] string BookingService);
public sealed record IntegrationRate([Required, Range(0, long.MaxValue)] long? Revision, [Required] decimal? BaseRate);



