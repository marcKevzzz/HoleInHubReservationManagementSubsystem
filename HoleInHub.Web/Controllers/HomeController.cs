using System.Diagnostics;
using HoleInHub.Business.Services;
using HoleInHubReservationManagementSubsystem.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace HoleInHubReservationManagementSubsystem.Controllers;

public class HomeController(IServiceCatalog catalog) : Controller
{
    public IActionResult Index()
    {
        return View();
    }

    public IActionResult About() => View();
    public IActionResult Gallery() => View();
    public IActionResult Contacts() => View();
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

    public IActionResult Privacy()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}

