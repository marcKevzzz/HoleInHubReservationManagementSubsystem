using System.Diagnostics;
using HoleInHub.Business.Accounts;
using HoleInHub.Business.Services;
using HoleInHub.Business.Website;
using HoleInHubReservationManagementSubsystem.ViewModels;
using Microsoft.AspNetCore.Mvc;
using HoleInHub.Web.Accounts;
using Microsoft.AspNetCore.Authentication;

namespace HoleInHubReservationManagementSubsystem.Controllers;

public class HomeController(IServiceCatalog catalog, HoleInHub.Business.Settings.OperationalSettings? settings = null, WebsiteContentService? website = null) : Controller
{
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Index(CancellationToken ct) => View(await PublicContentAsync(ct));

    public IActionResult About() => View();

    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Gallery(CancellationToken ct) => View(await PublicContentAsync(ct));

    [HttpGet("/website-content/images/{id}")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> WebsiteImage(string id, CancellationToken ct)
    {
        try
        {
            var staff = await HttpContext.AuthenticateAsync(StaffPinAccess.Scheme);
            var admin = staff.Principal?.IsInRole(StaffRoles.Admin) == true;
            var image = website is null ? null : await website.ReadImageAsync(id, admin, ct);
            if (image is null)
            {
                return NotFound();
            }

            Response.Headers.XContentTypeOptions = "nosniff";
            return File(image.Value.Bytes, image.Value.ContentType);
        }
        catch (Exception error) when (ServiceAvailability.Translate(error) is not null)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
    }

    private async Task<WebsiteContentRecord> PublicContentAsync(CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        try
        {
            if (website is not null && await website.ReadAsync(timeout.Token) is { } state)
            {
                return state;
            }
            return WebsiteContentService.InitialContent();
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            return new WebsiteContentRecord();
        }
        catch (Exception error) when (ServiceAvailability.Translate(error) is not null)
        {
            return new WebsiteContentRecord();
        }
    }

    public async Task<IActionResult> Contacts(CancellationToken ct)
    {
        try { return View(settings is null ? null : await settings.GetAsync(ct)); }
        catch (Exception error) when (ServiceAvailability.Translate(error) is not null) { return View(); }
    }
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public async Task<IActionResult> Services(CancellationToken ct)
    {
        try { return View(await catalog.GetActiveAsync(ct)); }
        catch (Exception error) when (ServiceAvailability.Translate(error) is not null)
        {
            ViewData["CatalogUnavailable"] = true;
            return View(Array.Empty<ServiceCatalogItem>());
        }
    }

    [HttpGet]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public async Task<IActionResult> ServiceCatalog(CancellationToken ct)
    {
        try { return PartialView("_ServiceCatalog", await catalog.GetActiveAsync(ct)); }
        catch (Exception error) when (ServiceAvailability.Translate(error) is not null)
        { return StatusCode(StatusCodes.Status503ServiceUnavailable); }
    }

    public IActionResult Privacy() => Redirect("/policies#privacy");

    [HttpGet, ResponseCache(Duration = 300, Location = ResponseCacheLocation.Any)]
    public async Task<IActionResult> ChatPopular([FromServices] HoleInHub.Business.Reservations.ReservationService reservations, CancellationToken ct)
    {
        try
        {
            var (services, bays) = await reservations.PopularChoicesAsync(catalog, ct);
            return Json(new { services, bays });
        }
        catch (Exception error) when (ServiceAvailability.Translate(error) is not null)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
    }

    [HttpGet, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> ChatServices(CancellationToken ct)
    {
        try
        {
            var items = await catalog.GetActiveAsync(ct);
            return Json(items.Select(item => new { item.Title, item.Summary, item.RateText, item.Details, item.ActionUrl }));
        }
        catch (Exception error) when (ServiceAvailability.Translate(error) is not null)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}

