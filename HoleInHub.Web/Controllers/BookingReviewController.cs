using System.Globalization;
using System.Security.Claims;
using HoleInHub.Business.Accounts;
using HoleInHub.Business.Customers;
using HoleInHub.Business.Reservations;
using HoleInHub.Web.Accounts;
using HoleInHubReservationManagementSubsystem.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace HoleInHubReservationManagementSubsystem.Controllers;

[Authorize(Policy = StaffAuthorization.Workspace), ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class BookingReviewController(ReservationService reservations, TimeProvider clock, IOptions<AccountEmailOptions> emailOptions, CustomerProfileService customerProfiles) : Controller
{
    private DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), ReservationRules.Manila).DateTime);

    private static bool TryScheduleDate(string? date, out DateOnly day) =>
        DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out day)
        && day > DateOnly.MinValue && day < DateOnly.MaxValue;

    [HttpPost("/staff/reservations/{id}/move"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Move(string id, long? revision, string commandId, DateOnly? date, TimeOnly? startTime,
        string? resourceId, string? sourceResourceId, CancellationToken ct, bool confirmVipUpgrade = false)
    {
        var actor = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(actor)) { return Forbid(); }
        if (!ModelState.IsValid || revision is null || date is null || startTime is null) { return BadRequest(new { error = "Reload the schedule and choose a valid time." }); }
        try
        {
            await reservations.MoveStaffBookingAsync(id, revision.Value, commandId, date.Value, startTime.Value, actor, ct, resourceId, sourceResourceId, confirmVipUpgrade);
            new ToastNotice("success", confirmVipUpgrade ? "Booking upgraded to VIP" : "Booking moved", date.Value.ToString("d MMM") + " at " + startTime.Value.ToString("h:mm tt"),
                Url.Action(nameof(Details), new { id, date = date.Value.ToString("yyyy-MM-dd") }), "View booking").Store(TempData);
            return Json(new { redirect = Url.Action(nameof(Index), new { date = date.Value.ToString("yyyy-MM-dd") }) });
        }
        catch (ReservationValidationException error) { return Conflict(new { error = error.Message }); }
    }

    [HttpPost("/staff/reservations/batch-move"), ValidateAntiForgeryToken]
    public async Task<IActionResult> BatchMove(List<StaffBatchMoveItem> bookings, string commandId, DateOnly? date,
        int offsetMinutes, CancellationToken ct)
    {
        var actor = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(actor)) { return Forbid(); }
        if (!ModelState.IsValid || date is null)
        {
            return BadRequest(new { error = "Reload the schedule and choose a valid time." });
        }
        try
        {
            await reservations.MoveStaffBatchAsync(bookings, commandId, date.Value, offsetMinutes, actor, ct);
            new ToastNotice("success", "Bookings moved", $"{bookings.Count} reservations moved together.").Store(TempData);
            return Json(new { redirect = Url.Action(nameof(Index), new { date = date.Value.ToString("yyyy-MM-dd") }) });
        }
        catch (ReservationValidationException error) { return Conflict(new { error = error.Message }); }
    }

    [HttpGet]
    public async Task<IActionResult> Index(string? date, string? reference, CancellationToken ct, string? customerProfileId = null)
    {
        var today = Today;
        var validDate = TryScheduleDate(date, out var selected);
        var day = validDate ? selected : today;
        var dateError = date is not null && !validDate ? "Choose a valid date. Showing today’s schedule." : null;
        if (dateError is not null) { new ToastNotice("warning", "Invalid date", dateError).Store(TempData); }
        var lookup = await LookupBookingsAsync(day, reference, ct);

        await reservations.ProcessPendingAsync(ct);
        var bays = await reservations.GetStaffScheduleAsync(day, ct);
        var bookable = bays.Where(bay => bay.Active && bay.Service is ("Regular" or "VIP"))
            .Select(bay => new ResourceDto(bay.Id, bay.Name, bay.Service)).ToArray();
        var bookingForm = NewBookingForm(day, bookable);
        if (!string.IsNullOrWhiteSpace(customerProfileId))
        {
            var profile = await customerProfiles.GetAsync(customerProfileId, ct);
            if (profile is null) { return NotFound(); }
            bookingForm.CustomerProfileId = profile.Id;
            bookingForm.FirstName = profile.FirstName;
            bookingForm.LastName = profile.LastName;
            bookingForm.Phone = ContactRules.PhoneDigits(profile.Phone);
        }
        var model = new StaffWorkspaceModel(day, today, bays, lookup, dateError, bookingForm,
            User.ManagesVenue() ? NewBlockForm(day, bookable) : null);
        return Request.Headers["X-Staff-Schedule"] == "true" ? PartialView("_StaffSchedule", model) : View(model);
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
    public async Task<IActionResult> Create(string? date, CancellationToken ct, string? customerProfileId = null)
    {
        var day = DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var selected)
            ? selected : (DateOnly?)null;
        var form = NewBookingForm(day, await reservations.GetCatalogAsync(ct));
        if (!string.IsNullOrWhiteSpace(customerProfileId))
        {
            var profile = await customerProfiles.GetAsync(customerProfileId, ct);
            if (profile is null) { return NotFound(); }
            form.CustomerProfileId = profile.Id;
            form.FirstName = profile.FirstName; form.LastName = profile.LastName;
            form.Phone = HoleInHub.Business.Accounts.ContactRules.PhoneDigits(profile.Phone);
        }
        return View(form);
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
                var bays = form.ResourceIds.Length > 0 ? form.ResourceIds : [form.ResourceId ?? ""];
                var request = new StaffBookingRequest(bays[0], form.Date!.Value, form.StartTime!.Value,
                    form.DurationMinutes, form.FirstName, form.LastName, form.Phone, form.Attendees, form.Source, form.IdempotencyKey)
                { CustomerProfileId = form.CustomerProfileId, AdditionalResourceIds = bays.Length > 1 ? bays[1..] : null };
                var saved = await reservations.CreateStaffBookingAsync(request, actor, ct);
                var date = form.Date.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                new ToastNotice("success", "Booking confirmed", saved.Reference,
                    Url.Action(nameof(Details), new { id = saved.Id, date }), "View booking").Store(TempData);
                if (Request.Headers.Accept == "application/json")
                {
                    return Json(new { redirect = Url.Action(nameof(Index), new { date }) });
                }
                return RedirectToAction(nameof(Index), new { date });
            }
            catch (ReservationValidationException invalid) { error = invalid.Message; }
        }

        error ??= "Check the selected bays, date, time, guest details, and party size.";
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
    public async Task<IActionResult> CreateBlock(string? date, CancellationToken ct, bool wholeDay = false)
    {
        var day = DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var selected)
            ? selected : (DateOnly?)null;
        var form = NewBlockForm(day, await reservations.GetCatalogAsync(ct));
        form.WholeDay = wholeDay;
        if (wholeDay) { form.ResourceId = "*"; }
        return View(form);
    }

    [Authorize(Roles = StaffRoles.VenueManagement), HttpPost("/staff/reservations/blocks/new"), ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateBlock(StaffBlockForm form, CancellationToken ct)
    {
        if (form.WholeDay && form.Date is { } day)
        {
            form.StartTime = new TimeOnly(ReservationRules.OpeningHour(day.DayOfWeek), 0);
            form.DurationMinutes = 1440 - form.StartTime.Value.Hour * 60;
            ModelState.Remove(nameof(form.StartTime));
            ModelState.Remove(nameof(form.DurationMinutes));
        }
        var actor = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(actor)) { return Forbid(); }
        string? error = null;
        if (ModelState.IsValid)
        {
            try
            {
                await reservations.CreateScheduleBlockAsync(new StaffBlockRequest(form.ResourceId, form.Date!.Value,
                    form.StartTime!.Value, form.DurationMinutes, form.Reason, form.IdempotencyKey, form.WholeDay), actor, ct);
                new ToastNotice("success", form.WholeDay ? "Day blocked" : "Time blocked", "The selected bays are unavailable for bookings during this period.").Store(TempData);
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
                new ToastNotice(form.Block.ReleasedUtc is null ? "success" : "info",
                    form.Block.ReleasedUtc is null ? "Time block released" : "Time block already released", form.Block.Location).Store(TempData);
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

    [HttpGet("/staff/review-queue/counts")]
    public async Task<IActionResult> Counts([FromServices] NotificationDeliveryService delivery, CancellationToken ct)
    {
        var counts = await reservations.GetReviewCountsAsync(ct);
        if (!User.IsInRole(StaffRoles.Admin) || User.FindFirstValue(ClaimTypes.NameIdentifier) is not string staffId)
        {
            return Json(counts);
        }
        var adminCounts = new Dictionary<string, long>(counts)
        {
            ["notifications"] = await delivery.CountUnreadAsync(staffId, ct)
        };
        return Json(adminCounts);
    }

    [HttpGet("/staff/review-queue/{section?}", Name = "staff-review-section")]
    [HttpGet("/BookingReview/Queue")]
    public async Task<IActionResult> Queue(string? section, string? filter, string? date, string? day, string? sort, CancellationToken ct, int page = 1)
    {
        var cancellationSection = section ?? filter;
        if (cancellationSection is "cancellations" or "cancellation-requests")
        {
            return RedirectToAction(nameof(Cancellations), new { date, day, sort, view = cancellationSection == "cancellation-requests" ? "requests" : null, page = page > 1 ? (int?)page : null });
        }
        if (section == "closed" || section is null && filter is not null)
        {
            return RedirectToRoute("staff-review-section", new { section = StaffReviewQueue.SectionKey(section ?? filter), date, day, sort, page = page > 1 ? (int?)page : null });
        }
        if (section is not null && !StaffReviewQueue.Sections.Any(page => page.Key == section)) { return NotFound(); }
        var queue = await LoadQueueAsync(section, day, sort, ct, Math.Clamp(page, 1, 1000));
        return View(queue);
    }

    [HttpGet("/staff/cancellations")]
    public async Task<IActionResult> Cancellations(string? day, string? sort, CancellationToken ct, int page = 1, string? view = null) =>
        View("Queue", await LoadQueueAsync(view == "requests" ? "cancellation-requests" : "cancellations", day, sort, ct, Math.Clamp(page, 1, 1000)));

    private async Task<StaffReviewQueue> LoadQueueAsync(string? filter, string? day, string? sort, CancellationToken ct, int page = 0)
    {
        var section = StaffReviewQueue.SectionKey(filter);
        var validDay = TryScheduleDate(day, out var selected);
        var items = section == "expired"
            ? await reservations.GetExpiredRequestsAsync(validDay ? selected : null, ct, page, sort == "session")
            : await reservations.GetReviewQueueAsync(ct, section == "cancellations" ? "cancelled" : section == "cancellation-requests" ? "cancellations" : section,
                validDay ? selected : null, sort == "session", page);
        return new(page > 0 ? items.Take(20).ToArray() : items, section, validDay ? day : null, sort == "session" ? sort : "deadline", clock.GetUtcNow().UtcDateTime)
        { Page = page, HasMore = page > 0 && items.Count > 20 && page < 1000 };
    }

    [HttpGet]
    public async Task<IActionResult> Details(string id, string? date, bool fromQueue, string? filter, string? day, string? sort, CancellationToken ct, string? customerId = null, string? customerView = null, int customerPage = 1)
    {
        ViewData["CustomerId"] = customerId is { Length: > 0 and <= 100 } ? customerId : null;
        ViewData["CustomerView"] = customerView == "upcoming" ? "upcoming" : null;
        ViewData["CustomerPage"] = Math.Clamp(customerPage, 1, 1000);
        var context = await reservations.GetStaffReviewAsync(id, ct);
        if (context is null)
        {
            return NotFound();
        }
        ViewData["ChangeBays"] = (await reservations.GetCatalogAsync(ct)).Where(bay => bay.Service == context.Booking.Service).ToArray();
        var queue = fromQueue ? await LoadQueueAsync(filter, day, sort, ct)
            : new StaffReviewQueue([], StaffReviewQueue.DefaultSection, null, "deadline", clock.GetUtcNow().UtcDateTime);
        var ids = queue.Visible.Select(x => x.Id).ToList();
        var position = ids.IndexOf(id);
        var startDay = DateOnly.FromDateTime(StaffReviewQueue.Local(context.Booking.StartUtc));
        var bays = await reservations.GetStaffScheduleAsync(startDay, ct);
        return View(new StaffReviewDetail(context, queue, bays.Where(bay => context.Booking.ResourceIds.Contains(bay.Id)).ToArray(), fromQueue, date,
            position > 0 ? ids[position - 1] : null, position + 1 < ids.Count ? ids[position + 1] : null));
    }

    [HttpGet("/staff/reservations/{id}/actions")]
    public async Task<IActionResult> Actions(string id, string? date, string? filter, string? day, string? sort, CancellationToken ct, bool drawer = false)
    {
        ViewData["QueueActions"] = !drawer;
        var result = await Details(id, date, !drawer, filter, day, sort, ct);
        return result is ViewResult { Model: StaffReviewDetail detail }
            ? PartialView(drawer ? "_StaffDrawerActions" : "_ReviewDecision", detail) : result;
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DecideChange(string id, string proposalId, long? expectedRevision,
        string decision, string[] resources, string? reason, string? date, CancellationToken ct,
        bool fromQueue = false, string? day = null, string? sort = null, bool returnToQueue = false)
    {
        var actor = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(actor)) { return Forbid(); }
        if (!expectedRevision.HasValue || decision is not ("approve" or "decline")) { return BadRequest(); }
        var approved = decision == "approve";
        var queueAction = fromQueue && returnToQueue;
        try
        {
            var receipt = await reservations.DecideScheduleChangeAsync(id, proposalId, expectedRevision.Value, approved,
                resources, actor, reason, ct);
            var start = StaffReviewQueue.Local(receipt.StartUtc);
            var end = StaffReviewQueue.Local(receipt.EndUtc);
            var interval = $"{start:d MMM, h:mm tt}–{(start.Date == end.Date ? end.ToString("h:mm tt") : end.ToString("d MMM, h:mm tt"))}";
            var title = approved ? "Schedule change approved" : "Schedule change declined";
            var message = $"{receipt.Reference} · {(approved ? interval : $"Original time kept: {interval}")}";
            new ToastNotice("success", title, message).Store(TempData);
            if (queueAction) { return RedirectToRoute("staff-review-section", new { section = "changes", date, day, sort }); }
        }
        catch (ReservationValidationException error)
        {
            new ToastNotice("warning", "Schedule change not saved", error.Message).Store(TempData);
        }
        return RedirectToAction(nameof(Details), new { id, date, fromQueue, filter = fromQueue ? "changes" : null, day, sort });
    }

    [HttpPost("/staff/reservations/{id}/attendance"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Attendance(string id, StaffAttendanceForm form, string? date, CancellationToken ct,
        bool fromQueue = false, string? filter = null, string? day = null, string? sort = null)
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
                    var toast = result switch
                    {
                        AttendanceResult.Stale => new ToastNotice("warning", "Booking changed", "Review the latest details before trying again."),
                        AttendanceResult.AlreadyApplied => new ToastNotice("info",
                            form.Action == "check-in" ? "Guest already checked in" : "Guest already checked out"),
                        _ when form.Action == "check-in" => new ToastNotice("success", "Guest checked in"),
                        _ => new ToastNotice("success", "Guest checked out", "Remaining bay time was released.")
                    };
                    toast.Store(TempData);
                    return RedirectToAction(nameof(Details), new { id, date, fromQueue, filter, day, sort });
                }
                ModelState.AddModelError("", "This action is unavailable for the current state, session time or bay availability. Review the latest booking details.");
            }
            catch (ReservationValidationException error) { ModelState.AddModelError("", error.Message); }
        }
        var page = await Details(id, date, fromQueue, filter, day, sort, ct);
        return page is ViewResult { Model: StaffReviewDetail detail }
            ? View("Details", detail with { AttendanceForm = form }) : page;
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Decide(string id, string decision, string? reason, string? reasonChoice, string? date, bool fromQueue,
        string? filter, string? day, string? sort, long? expectedRevision, CancellationToken ct, bool returnToQueue = false,
        BayParticipants[]? participants = null)
    {
        if (!expectedRevision.HasValue || expectedRevision.Value < 0) { return BadRequest(); }
        var actor = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(actor)) { return Forbid(); }
        if (decision is not ("approve" or "reject" or "coach" or "cancel" or "keep" or "reschedule"))
        {
            return BadRequest();
        }

        try
        {
            if (decision == "approve" && !ModelState.IsValid)
            { throw new ReservationValidationException("Enter whole numbers for players and non-players on each bay."); }
            if (decision == "reject" && reasonChoice is ("Bay unavailable" or "Outside operating hours" or "Can’t reach customer"))
            {
                reason = string.IsNullOrWhiteSpace(reason) ? reasonChoice : $"{reasonChoice}. {reason.Trim()}";
            }
            string? rebookUrl = null;
            if (decision == "reschedule")
            {
                if (!Uri.TryCreate(emailOptions.Value.PublicBaseUrl, UriKind.Absolute, out var baseUrl)
                    || baseUrl.Scheme != Uri.UriSchemeHttps && !(baseUrl.IsLoopback && baseUrl.Scheme == Uri.UriSchemeHttp))
                {
                    throw new ReservationValidationException("Set the public booking URL before sending a reschedule request.");
                }
                rebookUrl = new Uri(baseUrl, Url.Action("Create", "Reservations")!).AbsoluteUri;
            }
            var changed = await reservations.ReviewAsync(id, decision, actor, ct, reason, expectedRevision, rebookUrl, participants);
            var toast = changed ? decision switch
            {
                "approve" => new ToastNotice("success", "Booking approved"),
                "reject" => new ToastNotice("success", "Booking rejected"),
                "coach" => new ToastNotice("success", "Coach confirmation recorded"),
                "cancel" => new ToastNotice("success", "Cancellation approved", "The reserved bay time was released."),
                "keep" => new ToastNotice("success", "Cancellation declined", "The reservation remains scheduled."),
                _ => new ToastNotice("success", "Reschedule requested")
            } : new ToastNotice("warning", "Booking changed", "Review the latest details before trying again.");
            toast.Store(TempData);
            if (changed && fromQueue && returnToQueue && filter is ("cancellations" or "cancellation-requests") && (decision is "cancel" or "keep"))
            {
                return RedirectToAction(nameof(Cancellations), new { date, day, sort, view = filter == "cancellation-requests" ? "requests" : null });
            }
        }
        catch (ReservationValidationException error)
        {
            new ToastNotice("warning", "Decision not saved", error.Message).Store(TempData);
        }
        return RedirectToAction(nameof(Details), new { id, date, fromQueue = fromQueue ? true : (bool?)null, filter, day, sort });
    }
}
