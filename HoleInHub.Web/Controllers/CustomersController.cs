using System.Security.Claims;
using HoleInHub.Business.Customers;
using HoleInHub.Business.Reservations;
using HoleInHub.Web.Accounts;
using HoleInHubReservationManagementSubsystem.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HoleInHubReservationManagementSubsystem.Controllers;

[Authorize(Policy = StaffAuthorization.Workspace), ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("staff/customers")]
public sealed class CustomersController(ReservationService reservations, CustomerProfileService profiles) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(string? search, int page = 1, CancellationToken ct = default)
    {
        ViewData["PendingReviews"] = await reservations.GetPendingReviewCountAsync(ct);
        ViewData["CustomerMergeReady"] = await profiles.InstalledAsync(ct);
        var term = search?.Trim() ?? "";
        if (term.Length == 1)
        {
            ViewData["SearchHint"] = "Type one more character to search.";
            return View(new StaffCustomerDirectory([], term, 1, false));
        }
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
        ViewData["CustomerProfile"] = await profiles.GetAsync(id, ct);
        return View(customer);
    }

    [HttpGet("{id}/merge")]
    public async Task<IActionResult> Merge(string id, string? target, string? search, int page = 1, CancellationToken ct = default)
    {
        if (!await profiles.InstalledAsync(ct)) { return StatusCode(503, "Apply the customer-profile database update before combining records."); }
        var source = await profiles.GetAsync(id, ct);
        if (source is null) { return NotFound(); }
        var term = search?.Trim() ?? "";
        StaffCustomerDirectory candidates = new([], term, Math.Clamp(page, 1, 1000), false);
        string? error = null;
        if (term.Length == 1) { error = "Type one more character to search."; }
        else if (string.IsNullOrWhiteSpace(target))
        {
            try { candidates = await reservations.SearchCustomersAsync(term, page, ct); }
            catch (ReservationValidationException ex) { error = ex.Message; }
        }
        CustomerMergePreview? preview = null;
        if (!string.IsNullOrWhiteSpace(target))
        {
            try { preview = await profiles.PreviewAsync(target, source.Id, ct); }
            catch (ReservationValidationException ex) { error = ex.Message; }
            if (preview is null && error is null) { error = "That customer record is unavailable. Choose it again."; }
        }
        var form = preview is null ? new CustomerMergeForm() : new CustomerMergeForm
        {
            SourceId = preview.Source.Id,
            TargetId = preview.Target.Id,
            SourceContactRevision = preview.Source.ContactRevision,
            SourceMembershipRevision = preview.Source.MembershipRevision,
            TargetContactRevision = preview.Target.ContactRevision,
            TargetMembershipRevision = preview.Target.MembershipRevision,
            ReservationIds = preview.SourceBookings.Select(b => b.Id).ToArray(),
            BookingScope = preview.Source.BookingCount > CustomerProfileService.MaximumMergeBookings ? "selected" : "all",
            NameFrom = string.IsNullOrWhiteSpace(preview.Target.FirstName + preview.Target.LastName) ? "source" : "target",
            PhoneFrom = string.IsNullOrWhiteSpace(preview.Target.Phone) ? "source" : "target",
            EmailFrom = string.IsNullOrWhiteSpace(preview.Target.Email) ? "source" : "target"
        };
        return RenderMerge(new CustomerMergeModel(source, candidates, preview, form, term, error));
    }

    [HttpPost("{id}/merge"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Merge(string id, [FromForm] CustomerMergeForm form, CancellationToken ct)
    {
        var actor = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (actor is null) { return Forbid(); }
        if (form.BookingScope is not ("all" or "selected")) { ModelState.AddModelError("Form.BookingScope", "Choose all bookings or an explicit selection."); }
        if (ModelState.IsValid)
        {
            try
            {
                var receipt = await profiles.MergeAsync(form.Command(), actor, User.Identity?.Name ?? actor, ct);
                var reservationLabel = receipt.BookingCount == 1 ? "reservation" : "reservations";
                var toast = new ToastNotice("success", "Customer records merged",
                    $"{receipt.BookingCount} {reservationLabel} added to the staff profile.");
                if (IsDialogMerge)
                {
                    var profile = await profiles.GetAsync(receipt.ProfileId, ct);
                    return Json(new
                    {
                        toast = toast with
                        {
                            Href = Url.Action(nameof(MergeHistory), new { id = receipt.ProfileId }),
                            Action = "View merge history"
                        },
                        profileId = receipt.ProfileId,
                        rowKey = profile?.BookingId,
                        detailsUrl = Url.Action(nameof(Details), new { id = receipt.ProfileId })
                    });
                }
                toast.Store(TempData);
                return RedirectToAction(nameof(Details), new { id = receipt.ProfileId });
            }
            catch (ReservationValidationException ex) { ModelState.AddModelError("", ex.Message); }
        }
        Response.StatusCode = StatusCodes.Status409Conflict;
        var source = await profiles.GetAsync(id, ct);
        if (source is null) { return NotFound(); }
        CustomerMergePreview? preview = null;
        try { preview = await profiles.PreviewAsync(form.SourceId, form.TargetId, ct); }
        catch (ReservationValidationException ex) { ModelState.AddModelError("", ex.Message); }
        return RenderMerge(new CustomerMergeModel(source, new([], "", 1, false), preview, form, "", "Review the message below. Reload the comparison if these records changed."));
    }

    private bool IsDialogMerge => Request.Headers["X-Customer-Merge"] == "dialog";

    private IActionResult RenderMerge(CustomerMergeModel model)
    {
        ViewData["RowMergeDialog"] = IsDialogMerge;
        return IsDialogMerge ? PartialView("_CustomerMergeContent", model) : View("Merge", model);
    }

    [HttpGet("{id}/merge-history")]
    public async Task<IActionResult> MergeHistory(string id, int page = 1, CancellationToken ct = default)
    {
        var history = await profiles.HistoryAsync(id, page, ct);
        return history is null ? NotFound() : View(history);
    }

    [HttpPost("{id}/undo-merge"), ValidateAntiForgeryToken]
    public async Task<IActionResult> UndoMerge(string id, [FromForm] CustomerUndoForm form, CancellationToken ct)
    {
        var actor = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (actor is null) { return Forbid(); }
        if (ModelState.IsValid)
        {
            try
            {
                var receipt = await profiles.UndoAsync(form.OperationId, form.CommandId, form.Reason, actor, User.Identity?.Name ?? actor, ct);
                new ToastNotice("success", "Merge undone", "Customer profiles and reservation histories were restored.")
                    .Store(TempData);
                return RedirectToAction(nameof(MergeHistory), new { id = receipt.ProfileId });
            }
            catch (ReservationValidationException ex) { ModelState.AddModelError("", ex.Message); }
        }
        Response.StatusCode = StatusCodes.Status409Conflict;
        var history = await profiles.HistoryAsync(id, 1, ct);
        return history is null ? NotFound() : View(nameof(MergeHistory), history);
    }
}
