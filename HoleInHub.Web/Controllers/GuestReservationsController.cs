using System.Security.Cryptography;
using HoleInHub.Business.Accounts;
using HoleInHub.Business.Reservations;
using HoleInHub.Web.Accounts;
using HoleInHub.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace HoleInHub.Web.Controllers;

[AllowAnonymous, Route("guest/reservations"), ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class GuestReservationsController(EmailVerificationService codes, ReservationService reservations,
    GuestReservationSession session, IDataProtectionProvider protection, TimeProvider clock, IOptions<ReservationPolicy> policy, IHostEnvironment environment) : Controller
{
    public override void OnActionExecuting(Microsoft.AspNetCore.Mvc.Filters.ActionExecutingContext context)
    {
        Response.Headers["Referrer-Policy"] = "no-referrer";
        base.OnActionExecuting(context);
    }
    private static string Scope(GuestReservationSession.Challenge challenge) => "guest-recovery:" + challenge.Id;
    private string Destination(string? value) => value == "/guest/reservations" ||
        value is not null && System.Text.RegularExpressions.Regex.IsMatch(value, "^/guest/reservations/details/[A-Za-z0-9_-]{1,100}$")
            ? value : "/guest/reservations";

    [HttpGet("find")]
    public IActionResult Find(string? returnUrl)
    {
        Response.Headers["Referrer-Policy"] = "no-referrer";
        return session.Email(HttpContext) is not null ? RedirectToAction(nameof(Index)) : View(new FindGuestReservationsModel { ReturnUrl = Destination(returnUrl) });
    }

    [HttpPost("find"), ValidateAntiForgeryToken, EnableRateLimiting("guest-recovery-send")]
    public async Task<IActionResult> Find(FindGuestReservationsModel model, CancellationToken ct)
    {
        model.ReturnUrl = Destination(model.ReturnUrl);
        if (ModelState.IsValid)
        {
            var challenge = new GuestReservationSession.Challenge(Convert.ToHexString(RandomNumberGenerator.GetBytes(32)), model.Email.Trim().ToLowerInvariant(), model.ReturnUrl, clock.GetUtcNow());
            try
            {
                await SendAsync(challenge, ct);
                session.Begin(HttpContext, challenge);
                return RedirectToAction(nameof(Verify));
            }
            catch (ReservationValidationException error) { ModelState.AddModelError("", error.Message); }
        }
        return View(model);
    }

    [HttpGet("verify")]
    public async Task<IActionResult> Verify(CancellationToken ct)
    {
        var challenge = session.Recovery(HttpContext);
        return challenge is null ? RedirectToAction(nameof(Find)) : View(await CodeModel(challenge, ct));
    }

    [HttpPost("verify"), ValidateAntiForgeryToken, EnableRateLimiting("guest-recovery-verify")]
    public async Task<IActionResult> Verify(string? code, CancellationToken ct)
    {
        var challenge = session.Recovery(HttpContext);
        if (challenge is null) { return RedirectToAction(nameof(Find)); }
        try
        {
            await codes.VerifyAsync(Scope(challenge), challenge.Email, code ?? "", ct, "GuestRecovery");
            session.Grant(HttpContext, challenge.Email);
            return LocalRedirect(Destination(challenge.ReturnUrl));
        }
        catch (ReservationValidationException) { ModelState.AddModelError("", "This code is incorrect or expired. After five unsuccessful attempts, request a new code."); }
        return View(await CodeModel(challenge, ct));
    }

    [HttpPost("resend"), ValidateAntiForgeryToken, EnableRateLimiting("guest-recovery-send")]
    public async Task<IActionResult> Resend(CancellationToken ct)
    {
        var challenge = session.Recovery(HttpContext);
        if (challenge is null) { return RedirectToAction(nameof(Find)); }
        try { await SendAsync(challenge, ct); ViewData["VerificationMessage"] = "A new code is on its way."; }
        catch (ReservationValidationException error) { ModelState.AddModelError("", error.Message); }
        return View("Verify", await CodeModel(challenge, ct));
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(string? view, int page = 1, CancellationToken ct = default)
    {
        Response.Headers["Referrer-Policy"] = "no-referrer";
        var email = session.Email(HttpContext);
        if (email is null) { return RedirectToAction(nameof(Find)); }
        page = Math.Clamp(page, 1, int.MaxValue / 20);
        return View(new GuestReservationsModel(email, await reservations.GetGuestHistoryAsync(email, view == "past", page, ct), view == "past", page));
    }

    [HttpGet("details/{id}")]
    public async Task<IActionResult> Details(string id, CancellationToken ct)
    {
        Response.Headers["Referrer-Policy"] = "no-referrer";
        var email = session.Email(HttpContext);
        if (email is null) { return RedirectToAction(nameof(Find), new { returnUrl = Destination("/guest/reservations/details/" + id) }); }
        await reservations.ProcessPendingAsync(ct);
        var booking = await reservations.GetGuestDetailAsync(email, id, ct);
        if (booking is null) { return NotFound(); }
        ViewData["GuestAccess"] = true;
        ViewData["DemoPayments"] = (environment.IsDevelopment() || environment.IsEnvironment("Demo")) && policy.Value.DemoPaymentsEnabled;
        return View("~/Views/Reservations/Details.cshtml", booking);
    }

    [HttpPost("leave"), HttpPost("change-email"), ValidateAntiForgeryToken]
    public IActionResult Leave()
    {
        session.Leave(HttpContext);
        return RedirectToAction(nameof(Find));
    }

    private async Task SendAsync(GuestReservationSession.Challenge challenge, CancellationToken ct)
    {
        var code = RandomNumberGenerator.GetInt32(100000, 1000000).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var body = "protected:" + protection.CreateProtector("BookingEmail").Protect($"Your Hole In Hub reservation access code is {code}. It expires in 10 minutes. Do not share it.");
        await codes.IssueAsync(Scope(challenge), challenge.Email, EmailVerificationService.CodeHash(Scope(challenge), challenge.Email, code), body,
            "Access your Hole In Hub reservations", "GuestRecovery", ct);
    }

    private async Task<EmailCodeFormModel> CodeModel(GuestReservationSession.Challenge challenge, CancellationToken ct)
    {
        var code = await codes.ReadAsync(Scope(challenge), ct);
        var now = clock.GetUtcNow().UtcDateTime;
        return new()
        {
            Email = challenge.Email,
            ActionUrl = "/guest/reservations/verify",
            ResendUrl = "/guest/reservations/resend",
            SubmitLabel = "View reservations",
            ExpiresUtc = code?.ExpiresUtc ?? now,
            ResendAfterUtc = code?.ResendAfterUtc ?? now,
            ServerNowUtc = now,
            Expired = code is null || code.ExpiresUtc <= now,
            ResendWhenExpired = true
        };
    }
}
