using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using HoleInHub.Business.Accounts;
using HoleInHub.Business.Reservations;
using HoleInHub.Business.Services;
using HoleInHub.Web.Accounts;
using HoleInHubReservationManagementSubsystem.Localization;
using HoleInHubReservationManagementSubsystem.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
namespace HoleInHubReservationManagementSubsystem.Controllers;

public sealed class ReservationsController(ReservationService reservations, GuestBookingService guests,
    IDataProtectionProvider protection, IOptions<ReservationPolicy> policy,
    IOptions<AccountEmailOptions> emailOptions, IHostEnvironment environment, TimeProvider clock, AccountService accounts, GuestReservationSession guestAccess,
    IServiceCatalog catalog) : Controller
{
    [AllowAnonymous] public IActionResult Availability() => NotFound();
    [AllowAnonymous, HttpGet]
    public async Task<IActionResult> CheckAvailability(DateOnly date, TimeOnly startTime, int durationMinutes, string service, CancellationToken ct)
    {
        var (type, entryId) = BookingChoice(service);
        try { return Json(await reservations.GetAvailabilityAsync(date, startTime, durationMinutes, type, ct, entryId)); }
        catch (ReservationValidationException error) { return BadRequest(new { message = CustomerText.Error(error.Message) }); }
    }

    private static (string Type, string? EntryId) BookingChoice(string? value)
    {
        var parts = (value ?? "").Split(':', 2);
        return (parts[0], parts.Length == 2 ? parts[1] : null);
    }
    [AllowAnonymous, HttpGet]
    public async Task<IActionResult> RentalQuote(DateOnly date, TimeOnly startTime, int durationMinutes, CancellationToken ct,
        string service = "WholeShop", int bayCount = 1)
    {
        var (type, entryId) = BookingChoice(service);
        try
        {
            var quote = await reservations.GetQuoteAsync(date, startTime, durationMinutes, type, entryId, bayCount, DemoAllowed, ct);
            return Json(new { rentalTotal = quote.Total, quote.DepositAmount, quote.Currency, quote.Lines,
                quoteKey = ReservationService.QuoteKey(quote, type, entryId, date, startTime, durationMinutes, bayCount) });
        }
        catch (ReservationValidationException error) { return BadRequest(new { message = CustomerText.Error(error.Message) }); }
    }
    [Authorize, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Index(string? view, CancellationToken ct)
    {
        await reservations.ProcessPendingAsync(ct);
        var records = await reservations.GetOwnedAsync(User.FindFirstValue(ClaimTypes.NameIdentifier)!, ct);
        var now = clock.GetUtcNow().UtcDateTime;
        return View(new MyReservationsViewModel { Upcoming = records.Where(x => x.IsUpcoming(now)).OrderBy(x => x.StartUtc).ToArray(), Past = records.Where(x => !x.IsUpcoming(now)).OrderByDescending(x => x.StartUtc).ToArray(), ShowPast = view == "past" });
    }
    [Authorize] public async Task<IActionResult> Details(string id, CancellationToken ct) { Response.Headers.CacheControl = "no-store"; ViewData["DemoPayments"] = DemoAllowed; await reservations.ProcessPendingAsync(ct); var booking = await reservations.GetOwnedAsync(User.FindFirstValue(ClaimTypes.NameIdentifier)!, id ?? "", ct); return booking is null ? NotFound() : View(booking); }
    [AllowAnonymous]
    public async Task<IActionResult> Create(DateOnly? date, TimeOnly? startTime, int? durationMinutes, string? service, string? resourceId, bool editDraft, bool recover, CancellationToken ct)
    {
        var resources = await reservations.GetCatalogAsync(ct);
        ViewData["Resources"] = resources;
        var (type, entryId) = BookingChoice(service);
        var model = new SubmitReservationRequest
        {
            Date = date ?? DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, ReservationRules.Manila).DateTime.AddDays(1)),
            StartTime = startTime ?? new TimeOnly(11, 0),
            DurationMinutes = durationMinutes ?? 60,
            Service = ServiceCatalog.BookingTypes.Contains(type) ? type : "Regular",
            ServiceEntryId = ServiceCatalog.BookingTypes.Contains(type) ? entryId : null,
            IdempotencyKey = Guid.NewGuid().ToString("N")
        };
        if (model.Service == "WholeShop" && startTime == null && durationMinutes == null)
        {
            model.StartTime = new TimeOnly(15, 0); model.DurationMinutes = 360;
        }
        if (model.Service == "KpgaGroup" && startTime == null) { model.StartTime = new TimeOnly(15, 0); }
        if (editDraft && User.Identity?.IsAuthenticated != true)
        {
            var draft = await guests.DraftAsync(GuestSession(), ct);
            if (draft != null)
            {
                try { model = JsonSerializer.Deserialize<SubmitReservationRequest>(protection.CreateProtector("BookingDraft").Unprotect(draft.ProtectedDraft)) ?? model; }
                catch (Exception error) when (error is CryptographicException or JsonException) { ModelState.AddModelError("", "Your saved request expired. Enter your booking details again."); }
            }
        }
        var recovered = false;
        if (recover && TempData.Peek("BookingRecovery") is string saved)
        {
            try
            {
                var recovery = JsonSerializer.Deserialize<BookingRecoveryState>(saved);
                if (recovery != null && recovery.SavedUtc <= clock.GetUtcNow() && clock.GetUtcNow() - recovery.SavedUtc <= TimeSpan.FromMinutes(30))
                {
                    model = recovery.Request;
                    recovered = true;
                    ViewData["RecoveryMessage"] = "Your booking details were restored. Review availability and retry submission.";
                }
            }
            catch (JsonException) { ModelState.AddModelError("", "Your saved request expired. Enter your booking details again."); }
        }
        if (date.HasValue && startTime.HasValue || editDraft || recover)
        {
            try { await reservations.PreviewBookingIntervalAsync(model.Date, model.StartTime, model.DurationMinutes, model.Service, ct, model.ServiceEntryId); }
            catch (ReservationValidationException error) { ModelState.AddModelError("", error.Message); }
        }
        if (resourceId is not null && resources.Any(resource => resource.Id == resourceId && resource.Service == model.Service))
        {
            model.ResourceIds = [resourceId];
        }
        if (User.Identity?.IsAuthenticated == true)
        {
            if (!recovered)
            {
                var account = await accounts.FindByIdAsync(User.FindFirstValue(ClaimTypes.NameIdentifier)!, ct);
                var contact = account is not null && !string.IsNullOrWhiteSpace(account.FirstName) && !string.IsNullOrWhiteSpace(account.LastName) && !string.IsNullOrWhiteSpace(account.Phone)
                    ? null : await reservations.GetLatestContactAsync(User.FindFirstValue(ClaimTypes.NameIdentifier)!, ct);
                model.FirstName = !string.IsNullOrWhiteSpace(account?.FirstName) ? account.FirstName : contact?.FirstName ?? "";
                model.LastName = !string.IsNullOrWhiteSpace(account?.LastName) ? account.LastName : contact?.LastName ?? "";
                model.Phone = ContactRules.PhoneDigits(!string.IsNullOrWhiteSpace(account?.Phone) ? account.Phone : contact?.Phone);
            }
            model.Email = User.FindFirstValue(ClaimTypes.Email);
        }
        await PrepareAsync(model, ct);
        if (recover) { TempData.Remove("BookingRecovery"); }
        return View(model);
    }
    private bool DemoAllowed => (environment.IsDevelopment() || environment.IsEnvironment("Demo")) && policy.Value.DemoPaymentsEnabled;
    private string GuestSession()
    {
        var value = Request.Cookies["hih-booking"];
        if (value is { Length: 64 } && value.All(Uri.IsHexDigit))
        {
            return value;
        }

        value = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        Response.Cookies.Append("hih-booking", value, new CookieOptions { HttpOnly = true, Secure = !environment.IsDevelopment() || Request.IsHttps, SameSite = SameSiteMode.Lax, MaxAge = TimeSpan.FromHours(8), IsEssential = true });
        return value;
    }
    private async Task PrepareAsync(SubmitReservationRequest request, CancellationToken ct)
    {
        ViewData["Resources"] = await reservations.GetCatalogAsync(ct);
        ViewData["BookingServices"] = await catalog.GetActiveAsync(ct);
        GuestSession();
        ViewData["NowUtc"] = clock.GetUtcNow().ToString("O");
        ViewData["DemoPayments"] = DemoAllowed;
        Response.Headers.CacheControl = "no-store";
    }
    [HttpPost, AllowAnonymous, ValidateAntiForgeryToken, EnableRateLimiting("accounts")]
    public async Task<IActionResult> Create(SubmitReservationRequest request, CancellationToken ct)
    {
        var (type, entryId) = BookingChoice(request.Service);
        request.Service = type;
        request.ServiceEntryId = entryId;
        if (!ContactRules.IsName(request.FirstName)) { ModelState.AddModelError(nameof(request.FirstName), ContactRules.NameError); }
        if (!ContactRules.IsName(request.LastName)) { ModelState.AddModelError(nameof(request.LastName), ContactRules.NameError); }
        if (!ContactRules.IsPhone(request.Phone)) { ModelState.AddModelError(nameof(request.Phone), ContactRules.PhoneError); }
        if (User.Identity?.IsAuthenticated == true) { request.Email = User.FindFirstValue(ClaimTypes.Email); ModelState.Remove(nameof(request.Email)); }
        if (request.Service == "WholeShop") { request.ResourceIds = (await reservations.GetCatalogAsync(ct)).Select(x => x.Id).ToArray(); }
        // The public form collects one total; never fabricate a player/non-player split.
        request.Participants = [];
        var session = GuestSession();
        request.Email = request.Email?.Trim().ToLowerInvariant();
        if (ModelState.IsValid)
        {
            try
            {
                if (User.Identity?.IsAuthenticated == true)
                {
                    if (!User.HasClaim("email_verified", "true")) { throw new ReservationValidationException("Verify your email before submitting a reservation."); }
                    return await CompleteAsync(request, session, User.FindFirstValue(ClaimTypes.Email) ?? "", ct);
                }
                var received = await reservations.FindSubmittedAsync(request, "guest:" + GuestBookingService.Hash(session), request.Email ?? "", DemoAllowed, ct);
                if (received != null) { return RedirectToAction(nameof(Confirmation), new { id = received.Id, token = GuestBookingService.AccessToken(session, request.IdempotencyKey) }); }
                await reservations.ValidateSubmissionAsync(request, DemoAllowed, ct);
                if (await guests.VerifiedEmailAsync(session, ct) == request.Email)
                {
                    return await CompleteAsync(request, session, request.Email!, ct, true);
                }
                await SendCodeAsync(session, request, ct);
                return RedirectToAction(nameof(Verify));
            }
            catch (ReservationValidationException error) { ModelState.AddModelError("", error.Message); }
        }
        await PrepareAsync(request, ct);
        return View(request);
    }
    private async Task SendCodeAsync(string session, SubmitReservationRequest request, CancellationToken ct)
    {
        var email = request.Email ?? "";
        var code = RandomNumberGenerator.GetInt32(100000, 1000000).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var body = "protected:" + protection.CreateProtector("BookingEmail").Protect($"Your Hole In Hub booking code is {code}. It expires in 10 minutes. Do not share it.");
        var draft = protection.CreateProtector("BookingDraft").Protect(JsonSerializer.Serialize(request));
        await guests.IssueAsync(session, email, GuestBookingService.CodeHash(session, email, code), body, ct, draft);
    }
    [AllowAnonymous, HttpGet]
    public async Task<IActionResult> Verify(CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var draft = await guests.DraftAsync(GuestSession(), ct);
        return View(VerificationModel(draft));
    }
    [AllowAnonymous, HttpPost, ValidateAntiForgeryToken, EnableRateLimiting("accounts")]
    public async Task<IActionResult> Verify(string? code, string? intent, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var session = GuestSession();
        var draft = await guests.DraftAsync(session, ct);
        if (draft == null) { return View(new BookingVerificationViewModel { Expired = true }); }
        SubmitReservationRequest? request;
        try { request = JsonSerializer.Deserialize<SubmitReservationRequest>(protection.CreateProtector("BookingDraft").Unprotect(draft.ProtectedDraft)); }
        catch (Exception error) when (error is CryptographicException or JsonException) { return View(new BookingVerificationViewModel { Expired = true }); }
        if (request == null) { return View(new BookingVerificationViewModel { Expired = true }); }
        try
        {
            if (intent == "resend")
            {
                await SendCodeAsync(session, request, ct);
                ViewData["VerificationMessage"] = "A new code has been queued. Check your inbox shortly.";
                draft = await guests.DraftAsync(session, ct) ?? draft;
            }
            else
            {
                if (!draft.Verified) { await guests.VerifyAsync(session, draft.Email, code ?? "", ct); }
                return await CompleteAsync(request, session, draft.Email, ct, true);
            }
        }
        catch (ReservationValidationException error) { ModelState.AddModelError("", error.Message); }
        return View(VerificationModel(await guests.DraftAsync(session, ct)));
    }
    private BookingVerificationViewModel VerificationModel(GuestBookingDraftDto? draft) => new()
    {
        Email = draft?.Email ?? "",
        Expired = draft == null,
        Verified = draft?.Verified == true,
        ExpiresUtc = draft?.ExpiresUtc,
        ResendAfterUtc = draft?.ResendAfterUtc,
        ServerNowUtc = clock.GetUtcNow().UtcDateTime
    };
    private async Task<IActionResult> CompleteAsync(SubmitReservationRequest request, string session, string verifiedEmail, CancellationToken ct, bool forceGuest = false)
    {
        var account = User.Identity?.IsAuthenticated == true && !forceGuest;
        var access = GuestBookingService.AccessToken(session, request.IdempotencyKey);
        var owner = account ? User.FindFirstValue(ClaimTypes.NameIdentifier)! : "guest:" + GuestBookingService.Hash(session);
        var publicBase = emailOptions.Value.PublicBaseUrl;
        if (!Uri.TryCreate(publicBase, UriKind.Absolute, out var baseUri) || (baseUri.Scheme != "https" && !(environment.IsDevelopment() && baseUri.Scheme == "http")))
        {
            throw new ReservationValidationException("Booking email links are not configured. Please contact the venue.");
        }
        var statusPath = account ? "/Reservations" : $"/Reservations/Status?token={access}";
        var start = TimeZoneInfo.ConvertTimeToUtc(request.Date.ToDateTime(request.StartTime), ReservationRules.Manila);
        var localStart = TimeZoneInfo.ConvertTimeFromUtc(start, ReservationRules.Manila);
        var receipt = "protected:" + protection.CreateProtector("BookingEmail").Protect(
            $"Hi {request.FirstName.Trim()},\n\nYour reservation request has been received.\nReservation status: Pending\nExperience: {ReservationLabels.Service(request.Service)}\nSession: {localStart:dd MMM yyyy h:mm tt} (Asia/Manila)\nNumber of guests: {request.Attendees}\n\n" +
            (request.Service == "WholeShop" ? "Complete the 50% deposit before the deadline shown in your booking. No real money is collected.\n\n" : "Your request is awaiting staff approval. Review runs during operating hours and resumes at the next opening. Your selected bays are held while Pending. Check your booking for the review deadline. This email does not confirm your reservation.\n\n") +
            $"View your reservation reference, details and updates: {publicBase.TrimEnd('/')}{statusPath}");
        var result = await reservations.SubmitAsync(request, owner, verifiedEmail, ct,
            account ? null : GuestBookingService.Hash(session), account ? null : GuestBookingService.Hash(access), DemoAllowed, receipt);
        return RedirectToAction(nameof(Confirmation), new { id = result.Id, token = account ? null : access });
    }
    [AllowAnonymous, HttpGet]
    public async Task<IActionResult> Confirmation(string id, string? token, CancellationToken ct)
    {
        var booking = await AccessibleAsync(id, token, ct);
        ViewData["AccessToken"] = token;
        ViewData["DemoPayments"] = DemoAllowed;
        return booking == null ? NotFound() : View(booking);
    }
    private async Task<ReservationDto?> AccessibleAsync(string? id, string? token, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        if (!string.IsNullOrEmpty(token))
        {
            return token.Length == 64 ? await reservations.GetAccessAsync(id ?? "", GuestBookingService.Hash(token), ct) : null;
        }

        var owned = User.Identity?.IsAuthenticated == true ? await reservations.GetOwnedAsync(User.FindFirstValue(ClaimTypes.NameIdentifier)!, id ?? "", ct) : null;
        if (owned is not null) { return owned; }
        var email = guestAccess.Email(HttpContext);
        var guestBooking = email is not null ? await reservations.GetGuestDetailAsync(email, id ?? "", ct) : null;
        if (guestBooking is not null) { ViewData["GuestAccess"] = true; }
        return guestBooking;
    }
    private IActionResult MissingAccess(string? id, string? token) => string.IsNullOrEmpty(token) && (Request.Query["guest"] == "true" || Request.Cookies.ContainsKey(GuestReservationSession.AccessCookie)) && guestAccess.Email(HttpContext) is null
        ? RedirectToAction("Find", "GuestReservations", new { returnUrl = "/guest/reservations/details/" + Uri.EscapeDataString(id ?? "") }) : NotFound();
    [AllowAnonymous, HttpGet]
    public async Task<IActionResult> Status(string? id, string? token, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(token) && User.Identity?.IsAuthenticated == true)
        {
            await reservations.ProcessPendingAsync(ct);
        }
        var booking = await AccessibleAsync(id, token, ct);
        ViewData["AccessToken"] = token;
        ViewData["DemoPayments"] = DemoAllowed;
        return booking == null ? MissingAccess(id, token) : View("Details", booking);
    }
    [AllowAnonymous, HttpGet]
    public async Task<IActionResult> Checkout(string? id, string? token, CancellationToken ct)
    {
        if (!DemoAllowed)
        {
            return NotFound();
        }

        var booking = await AccessibleAsync(id, token, ct);
        ViewData["AccessToken"] = token;
        return booking is null ? MissingAccess(id, token) : booking.Service != "WholeShop" ? NotFound() : View(booking);
    }
    [AllowAnonymous, HttpPost, ValidateAntiForgeryToken, EnableRateLimiting("accounts")]
    public async Task<IActionResult> Checkout(string id, string? token, string outcome, CancellationToken ct)
    {
        if (!DemoAllowed)
        {
            return NotFound();
        }

        var booking = await AccessibleAsync(id, token, ct);
        if (booking?.Service != "WholeShop")
        {
            return booking is null ? MissingAccess(id, token) : NotFound();
        }

        try
        {
            var accepted = await reservations.DemoPaymentAsync(id, outcome, DemoAllowed, ct);
            if (accepted && outcome == "success")
            {
                return RedirectToAction(nameof(Confirmation), new { id, token });
            }

            TempData["BookingMessage"] = accepted ? "Demo payment outcome recorded. No money was collected." : "This hold has ended. The late demo result requires reconciliation; it does not confirm a booking.";
        }
        catch (ReservationValidationException error) { TempData["BookingMessage"] = error.Message; }
        return ViewData["GuestAccess"] is true ? RedirectToAction("Details", "GuestReservations", new { id }) : RedirectToAction(nameof(Status), new { id, token });
    }
    [AllowAnonymous, HttpPost, ValidateAntiForgeryToken, EnableRateLimiting("accounts")]
    public async Task<IActionResult> RequestCancellation(string? id, string? token, CancellationToken ct)
    {
        var booking = await AccessibleAsync(id, token, ct);
        if (booking == null)
        {
            return MissingAccess(id, token);
        }

        var accepted = await reservations.ReviewAsync(booking.Id, "request-cancellation", "Customer", ct);
        TempData["BookingMessage"] = !accepted
            ? "Online cancellation is available until one hour before the session for Pending or Confirmed bookings."
            : booking.Service == "WholeShop"
                ? "Cancellation requested. Your booking remains allocated until staff approves. Deposit refunds are arranged with the venue."
                : "Reservation cancelled. Any remaining bay hold has been released.";
        return ViewData["GuestAccess"] is true ? RedirectToAction("Details", "GuestReservations", new { id }) : RedirectToAction(nameof(Status), new { id, token });
    }

    [AllowAnonymous, HttpGet]
    public async Task<IActionResult> Change(string? id, string? token, CancellationToken ct)
    {
        var booking = await AccessibleAsync(id, token, ct);
        if (booking is null) { return MissingAccess(id, token); }
        if (!ReservationService.CanRequestScheduleChange(booking, clock.GetUtcNow().UtcDateTime)) { return BadRequest("This booking is not eligible for a schedule change."); }
        ViewData["AccessToken"] = token;
        var start = TimeZoneInfo.ConvertTimeFromUtc(booking.StartUtc, ReservationRules.Manila);
        return View(new ScheduleChangeForm
        {
            Booking = booking,
            ExpectedRevision = booking.Revision,
            CommandId = Guid.NewGuid().ToString("N"),
            Date = DateOnly.FromDateTime(start),
            StartTime = TimeOnly.FromDateTime(start),
            DurationMinutes = (int)(booking.EndUtc - booking.StartUtc).TotalMinutes
        });
    }

    [AllowAnonymous, HttpPost, ValidateAntiForgeryToken, EnableRateLimiting("accounts")]
    public async Task<IActionResult> Change(string? id, string? token, ScheduleChangeForm form, CancellationToken ct)
    {
        var booking = await AccessibleAsync(id, token, ct);
        if (booking is null) { return MissingAccess(id, token); }
        ViewData["AccessToken"] = token;
        if (ModelState.IsValid)
        {
            try
            {
                await reservations.RequestScheduleChangeAsync(new(booking.Id, form.ExpectedRevision!.Value, form.CommandId,
                    form.Date!.Value, form.StartTime!.Value, form.DurationMinutes, form.Reason), "Customer", ct);
                TempData["BookingMessage"] = "Change requested. Your original booking stays reserved until staff approves.";
                return ViewData["GuestAccess"] is true ? RedirectToAction("Details", "GuestReservations", new { id })
                    : RedirectToAction(nameof(Status), new { id, token });
            }
            catch (ReservationValidationException error) { ModelState.AddModelError("", error.Message); }
        }
        form.Booking = booking;
        return View(form);
    }
}
