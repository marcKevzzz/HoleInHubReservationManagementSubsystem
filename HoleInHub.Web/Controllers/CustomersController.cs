using HoleInHub.Business.Reservations;
using HoleInHub.Web.Accounts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HoleInHubReservationManagementSubsystem.Controllers;

[Authorize(Policy = StaffAuthorization.Workspace), ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("staff/customers")]
public sealed class CustomersController(ReservationService reservations) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(string? search, int page = 1, CancellationToken ct = default)
    {
        ViewData["PendingReviews"] = await reservations.GetPendingReviewCountAsync(ct);
        try { return View(await reservations.SearchCustomersAsync(search, page, ct)); }
        catch (ReservationValidationException error)
        {
            ViewData["SearchError"] = error.Message;
            return View(new StaffCustomerDirectory([], search?.Length > 100 ? search[..100] : search ?? "", 1, false));
        }
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Details(string id, string? view, int page = 1, CancellationToken ct = default)
    {
        var customer = await reservations.GetCustomerProfileAsync(id, view == "upcoming", page, ct);
        if (customer is null) { return NotFound(); }
        ViewData["PendingReviews"] = await reservations.GetPendingReviewCountAsync(ct);
        return View(customer);
    }
}
