using System.Globalization;
using System.Security.Claims;
using HoleInHub.Business.Accounts;
using HoleInHub.Business.Reservations;
using HoleInHub.Web.Accounts;
using HoleInHubReservationManagementSubsystem.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HoleInHubReservationManagementSubsystem.Controllers;

[Authorize(Policy = StaffAuthorization.Workspace), ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class BookingReviewController(ReservationService reservations, TimeProvider clock) : Controller
{
    private DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), ReservationRules.Manila).DateTime);

    private static bool TryScheduleDate(string? date, out DateOnly day) =>
        DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out day)
        && day > DateOnly.MinValue && day < DateOnly.MaxValue;

    [HttpPost("/staff/reservations/{id}/move"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Move(string id, long? revision, string commandId, DateOnly? date, TimeOnly? startTime,
        string? resourceId, string? sourceResourceId, CancellationToken ct)
    {
        var actor = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(actor)) { return Forbid(); }
        if (!ModelState.IsValid || revision is null || date is null || startTime is null) { return BadRequest(new { error = "Reload the schedule and choose a valid time." }); }
        try
        {
            await reservations.MoveStaffBookingAsync(id, revision.Value, commandId, date.Value, startTime.Value, actor, ct, resourceId, sourceResourceId);
            TempData["BookingMessage"] = "Booking moved.";
            return Json(new { redirect = Url.Action(nameof(Index), new { date = date.Value.ToString("yyyy-MM-dd") }) });
        }
        catch (ReservationValidationException error) { return Conflict(new { error = error.Message }); }
    }

    [HttpGet]
    public async Task<IActionResult> Index(string? date, string? reference, CancellationToken ct)
    {
        var today = Today;
        var validDate = TryScheduleDate(date, out var selected);
        var day = validDate ? selected : today;
        var dateError = date is not null && !validDate ? "Choose a valid date. Showing today’s schedule." : null;
        var lookup = await LookupBookingsAsync(day, reference, ct);

        await reservations.ProcessPendingAsync(ct);
        ViewData["PendingReviews"] = await reservations.GetPendingReviewCountAsync(ct);
        var bays = await reservations.GetStaffScheduleAsync(day, ct);
        var bookable = bays.Where(bay => bay.Active && bay.Service is ("Regular" or "VIP"))
            .Select(bay => new ResourceDto(bay.Id, bay.Name, bay.Service)).ToArray();
        return View(new StaffWorkspaceModel(day, today, bays, lookup, dateError, NewBookingForm(day, bookable),
            User.ManagesVenue() ? NewBlockForm(day, bookable) : null));
    }

    [HttpGet("/staff/reservations/search")]
    public async Task<IActionResult> Search(string? date, string? reference, CancellationToken ct) =>
        PartialView("_StaffSearchResults", await LookupBookingsAsync(TryScheduleDate(date, out var day) ? day : Today, reference, ct));

    private async Task<StaffBookingLookup> LookupBookingsAsync(DateOnly day, string? reference, CancellationToken ct)
    {
        var search = reference?.Trim() ?? "";
        if (search.Length > 100) { return new(day, search[..100], [], "Enter a reference or guest name of 100 characters or fewer."); }
        if (search.Length == 0) { return new(day, "", [], null); }
        var matches = await reservations.SearchStaffBookingsAsync(search, ct);
        return new(day, search, matches, matches.Count == 0 ? "No booking matches that reference or guest name." : null);
    }

    [HttpGet("/staff/reservations/new")]
    public async Task<IActionResult> Create(string? date, CancellationToken ct)
    {
        var day = DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var selected)
            ? selected : (DateOnly?)null;
        return View(NewBookingForm(day, await reservations.GetCatalogAsync(ct)));
    }

    [HttpPost("/staff/reservations/new"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(StaffBookingForm form, CancellationToken ct)
    {
        var actor = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(actor))
        {
            return Forbid();
        }

        string? error = null;
        if (ModelState.IsValid)
        {
            try
            {
                var request = new StaffBookingRequest(form.ResourceId, form.Date!.Value, form.StartTime!.Value,
                    form.DurationMinutes, form.FirstName, form.LastName, form.Phone, form.Attendees, form.Source, form.IdempotencyKey);
                var saved = await reservations.CreateStaffBookingAsync(request, actor, ct);
                TempData["BookingMessage"] = $"Booking {saved.Reference} confirmed.";
                var date = form.Date.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                if (Request.Headers.Accept == "application/json")
                {
                    return Json(new { redirect = Url.Action(nameof(Index), new { date }) });
                }
                return RedirectToAction(nameof(Index), new { date });
            }
            catch (ReservationValidationException invalid) { error = invalid.Message; }
        }

        error ??= "Check the booking date, time, guest details, and party size.";
        if (Request.Headers.Accept == "application/json")
        {
            return UnprocessableEntity(new { error });
        }

        ModelState.AddModelError("", error);
        form.Bays = await reservations.GetCatalogAsync(ct);
        form.Today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), ReservationRules.Manila).DateTime);
        return View(form);
    }

    [Authorize(Roles = StaffRoles.VenueManagement), HttpGet("/staff/reservations/blocks/new")]
    public async Task<IActionResult> CreateBlock(string? date, CancellationToken ct)
    {
        var day = DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var selected)
            ? selected : (DateOnly?)null;
        return View(NewBlockForm(day, await reservations.GetCatalogAsync(ct)));
    }

    [Authorize(Roles = StaffRoles.VenueManagement), HttpPost("/staff/reservations/blocks/new"), ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateBlock(StaffBlockForm form, CancellationToken ct)
    {
        var actor = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(actor)) { return Forbid(); }
        string? error = null;
        if (ModelState.IsValid)
        {
            try
            {
                await reservations.CreateScheduleBlockAsync(new StaffBlockRequest(form.ResourceId, form.Date!.Value,
                    form.StartTime!.Value, form.DurationMinutes, form.Reason, form.IdempotencyKey), actor, ct);
                TempData["BookingMessage"] = "Time blocked. The selected bay is unavailable for new bookings during this period.";
                var date = form.Date.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                if (Request.Headers.Accept == "application/json")
                {
                    return Json(new { redirect = Url.Action(nameof(Index), new { date }) });
                }
                return RedirectToAction(nameof(Index), new { date });
            }
            catch (ReservationValidationException invalid) { error = invalid.Message; }
        }
        error ??= "Check the bay, date, time, duration and reason.";
        if (Request.Headers.Accept == "application/json") { return UnprocessableEntity(new { error }); }
        ModelState.AddModelError("", error);
        form.Bays = await reservations.GetCatalogAsync(ct);
        form.Today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), ReservationRules.Manila).DateTime);
        return View(form);
    }

    [Authorize(Roles = StaffRoles.VenueManagement), HttpGet("/staff/reservations/blocks/{id}")]
    public async Task<IActionResult> Block(string id, CancellationToken ct)
    {
        var block = await reservations.GetScheduleBlockAsync(id, ct);
        return block is null ? NotFound() : View(new ReleaseBlockForm { Block = block });
    }

    [Authorize(Roles = StaffRoles.VenueManagement), HttpPost("/staff/reservations/blocks/{id}/release"), ValidateAntiForgeryToken]
    public async Task<IActionResult> ReleaseBlock(string id, ReleaseBlockForm form, CancellationToken ct)
    {
        var actor = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(actor)) { return Forbid(); }
        form.Block = await reservations.GetScheduleBlockAsync(id, ct);
        if (form.Block is null) { return NotFound(); }
        if (ModelState.IsValid)
        {
            try
            {
                if (!await reservations.ReleaseScheduleBlockAsync(id, form.Reason, actor, ct)) { return NotFound(); }
                return RedirectToAction(nameof(Block), new { id });
            }
            catch (ReservationValidationException error) { ModelState.AddModelError("", error.Message); }
        }
        return View("Block", form);
    }

    private StaffBlockForm NewBlockForm(DateOnly? selected, IReadOnlyList<ResourceDto> bays)
    {
        var booking = NewBookingForm(selected, bays);
        return new StaffBlockForm
        {
            Date = booking.Date,
            Today = booking.Today,
            StartTime = booking.StartTime,
            Bays = bays,
            IdempotencyKey = Guid.NewGuid().ToString("N")
        };
    }

    private StaffBookingForm NewBookingForm(DateOnly? selected, IReadOnlyList<ResourceDto> bays)
    {
        var now = TimeZoneInfo.ConvertTime(clock.GetUtcNow(), ReservationRules.Manila);
        var today = DateOnly.FromDateTime(now.DateTime);
        var day = selected.HasValue && selected.Value >= today && selected.Value < DateOnly.MaxValue ? selected.Value : today;
        var opening = ReservationRules.OpeningHour(day.DayOfWeek);
        var start = day == today && now.Hour >= opening
            ? new TimeOnly(now.Hour, now.Minute / 30 * 30)
            : new TimeOnly(opening, 0);
        return new StaffBookingForm
        {
            Date = day,
            Today = today,
            StartTime = start,
            Bays = bays,
            IdempotencyKey = Guid.NewGuid().ToString("N")
        };
    }

    [HttpGet]
    public async Task<IActionResult> Queue(string? filter, string? day, string? sort, CancellationToken ct)
    {
        var queue = await LoadQueueAsync(filter, day, sort, ct);
        ViewData["PendingReviews"] = await reservations.GetPendingReviewCountAsync(ct);
        return View(queue);
    }

    private async Task<StaffReviewQueue> LoadQueueAsync(string? filter, string? day, string? sort, CancellationToken ct)
    {
        var validDay = TryScheduleDate(day, out var selected);
        var items = filter == "expired"
            ? await reservations.GetExpiredRequestsAsync(validDay ? selected : null, ct)
            : await reservations.GetReviewQueueAsync(ct);
        return new(items, filter is "pending" or "cancellations" or "deposit" or "changes" or "expired" ? filter : "all",
            validDay ? day : null, sort == "session" ? sort : "deadline", clock.GetUtcNow().UtcDateTime);
    }

    [HttpGet]
    public async Task<IActionResult> Details(string id, string? date, bool fromQueue, string? filter, string? day, string? sort, CancellationToken ct)
    {
        var context = await reservations.GetStaffReviewAsync(id, ct);
        if (context is null)
        {
            return NotFound();
        }
        ViewData["ChangeBays"] = (await reservations.GetCatalogAsync(ct)).Where(bay => bay.Service == context.Booking.Service).ToArray();
        var queue = fromQueue ? await LoadQueueAsync(filter, day, sort, ct)
            : new StaffReviewQueue([], "all", null, "deadline", clock.GetUtcNow().UtcDateTime);
        var ids = queue.Visible.Select(x => x.Id).ToList();
        var position = ids.IndexOf(id);
        var startDay = DateOnly.FromDateTime(StaffReviewQueue.Local(context.Booking.StartUtc));
        var bays = await reservations.GetStaffScheduleAsync(startDay, ct);
        ViewData["PendingReviews"] = await reservations.GetPendingReviewCountAsync(ct);
        return View(new StaffReviewDetail(context, queue, bays.Where(bay => context.Booking.ResourceIds.Contains(bay.Id)).ToArray(), fromQueue, date,
            position > 0 ? ids[position - 1] : null, position + 1 < ids.Count ? ids[position + 1] : null));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DecideChange(string id, string proposalId, long? expectedRevision,
        string decision, string[] resources, string? reason, string? date, CancellationToken ct)
    {
        var actor = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(actor)) { return Forbid(); }
        if (!expectedRevision.HasValue || decision is not ("approve" or "decline")) { return BadRequest(); }
        try
        {
            await reservations.DecideScheduleChangeAsync(id, proposalId, expectedRevision.Value, decision == "approve",
                resources, actor, reason, ct);
            TempData["BookingMessage"] = decision == "approve" ? "Schedule change approved." : "Schedule change declined. The original booking is unchanged.";
        }
        catch (ReservationValidationException error) { TempData["BookingMessage"] = error.Message; }
        return RedirectToAction(nameof(Details), new { id, date, fromQueue = true, filter = "changes" });
    }
    [HttpPost("/staff/reservations/{id}/attendance"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Attendance(string id, StaffAttendanceForm form, string? date, CancellationToken ct)
    {
        var actor = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(actor)) { return Forbid(); }
        if (ModelState.IsValid)
        {
            try
            {
                var result = await reservations.RecordAttendanceAsync(new AttendanceCommand(id, form.Action,
                    form.ExpectedRevision!.Value, form.CommandId, form.Reason), actor, ct);
                if (result == AttendanceResult.NotFound) { return NotFound(); }
                if (result is AttendanceResult.Applied or AttendanceResult.AlreadyApplied or AttendanceResult.Stale)
                {
                    TempData["BookingMessage"] = result == AttendanceResult.Stale
                        ? "This booking changed. Review the latest details before trying again."
                        : result == AttendanceResult.AlreadyApplied ? "This attendance action was already recorded."
                        : form.Action == "check-in" ? "Guest checked in. The scheduled end is unchanged." : "Guest checked out. Remaining bay capacity has been released.";
                    return RedirectToAction(nameof(Details), new { id, date });
                }
                ModelState.AddModelError("", "This action is unavailable for the current state, session time or bay availability. Review the latest booking details.");
            }
            catch (ReservationValidationException error) { ModelState.AddModelError("", error.Message); }
        }
        var page = await Details(id, date, false, null, null, null, ct);
        return page is ViewResult { Model: StaffReviewDetail detail }
            ? View("Details", detail with { AttendanceForm = form }) : page;
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Decide(string id, string decision, string? reason, string? reasonChoice, string? date, bool fromQueue,
        string? filter, string? day, string? sort, long? expectedRevision, CancellationToken ct)
    {
        if (!expectedRevision.HasValue || expectedRevision.Value < 0) { return BadRequest(); }
        var actor = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(actor)) { return Forbid(); }
        if (decision is not ("approve" or "reject" or "coach" or "cancel" or "keep"))
        {
            return BadRequest();
        }

        try
        {
            if (decision == "reject" && reasonChoice is ("Bay unavailable" or "Outside operating hours" or "Can’t reach customer"))
            {
                reason = string.IsNullOrWhiteSpace(reason) ? reasonChoice : $"{reasonChoice}. {reason.Trim()}";
            }
            var changed = await reservations.ReviewAsync(id, decision, actor, ct, reason, expectedRevision);
            TempData["BookingMessage"] = changed ? "Decision recorded." : "This booking changed or the action is no longer available. Review the latest details and try again.";
        }
        catch (ReservationValidationException error) { TempData["BookingMessage"] = error.Message; }
        return RedirectToAction(nameof(Details), new { id, date, fromQueue = fromQueue ? true : (bool?)null, filter, day, sort });
    }
}
