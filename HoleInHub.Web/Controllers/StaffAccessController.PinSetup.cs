using System.Security.Cryptography;
using System.Text.Json;
using HoleInHub.Business.Accounts;
using HoleInHub.Business.Reservations;
using HoleInHub.Web.Accounts;
using HoleInHub.Web.ViewModels;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace HoleInHubReservationManagementSubsystem.Controllers;

public sealed partial class StaffAccessController
{
    private const string SetupCookie = "__Host-HihPinSetup";
    private sealed record PinSetupContext(string Session, string UserId, string Email, string Stamp, string Desktop, DateTimeOffset CreatedUtc);
    private static string SetupScope(PinSetupContext context) => "staff-pin:" + context.Session;

    [AllowAnonymous, HttpGet("pin/setup"), DisableRateLimiting]
    public async Task<IActionResult> Setup()
    {
        if (!access.Enabled || access.IsPresentation) { return NotFound(); }
        if (await DesktopAsync() is null) { return DesktopRequired(); }
        return View("RequestPin", new StaffPinRequestForm());
    }

    [AllowAnonymous, HttpPost("pin/setup")]
    public async Task<IActionResult> Setup(StaffPinRequestForm form, CancellationToken ct)
    {
        if (!access.Enabled || access.IsPresentation) { return NotFound(); }
        var desktop = await DesktopAsync();
        if (desktop is null) { return DesktopRequired(); }
        if (!ModelState.IsValid) { return View("RequestPin", form); }
        if (!await pins.TakeDeviceAttemptAsync(desktop, ct)) { return RateLimited("RequestPin", form); }
        var employee = await users.FindByEmailAsync(form.Email.Trim());
        if (employee is not null && StaffPinAccess.GetPinRoles(employee).Any()
            && (await signIn.CheckPasswordSignInAsync(employee, form.Password, lockoutOnFailure: true)).Succeeded)
        {
            var context = new PinSetupContext(Convert.ToHexString(RandomNumberGenerator.GetBytes(32)), employee.Id,
                employee.Email, employee.SecurityStamp, desktop, clock.GetUtcNow());
            try
            {
                await SendPinCodeAsync(context, ct);
                var value = protection.CreateProtector("StaffPinSetupSession").Protect(JsonSerializer.Serialize(context));
                Response.Cookies.Append(SetupCookie, value, new CookieOptions
                { Path = "/", HttpOnly = true, Secure = true, SameSite = SameSiteMode.Strict, MaxAge = TimeSpan.FromMinutes(30), IsEssential = true });
                return RedirectToAction(nameof(VerifyPin));
            }
            catch (ReservationValidationException error) { ModelState.AddModelError("", error.Message); }
        }
        else { ModelState.AddModelError("", "Unable to continue. Check your work email and password, or contact your administrator."); }
        return View("RequestPin", form);
    }

    [AllowAnonymous, HttpGet("pin/verify"), DisableRateLimiting]
    public async Task<IActionResult> VerifyPin(CancellationToken ct)
    {
        if (!access.Enabled || access.IsPresentation) { return NotFound(); }
        if (await DesktopAsync() is null) { return DesktopRequired(); }
        var context = await ReadPinContextAsync();
        return context is null ? RedirectToAction(nameof(Setup)) : View(await PinCodeModelAsync(context, ct));
    }

    [AllowAnonymous, HttpPost("pin/verify")]
    public async Task<IActionResult> VerifyPin(string? code, string? intent, CancellationToken ct)
    {
        if (!access.Enabled || access.IsPresentation) { return NotFound(); }
        var desktop = await DesktopAsync();
        if (desktop is null) { return DesktopRequired(); }
        var context = await ReadPinContextAsync();
        if (context is null) { return RedirectToAction(nameof(Setup)); }
        if (!await pins.TakeDeviceAttemptAsync(desktop, ct)) { return RateLimited("VerifyPin", await PinCodeModelAsync(context, ct)); }
        try
        {
            if (intent == "resend")
            {
                await SendPinCodeAsync(context, ct);
                ViewData["VerificationMessage"] = "A new code is on its way.";
            }
            else
            {
                await verification.VerifyAsync(SetupScope(context), context.Email, code ?? "", ct, "AccountEmail");
                return RedirectToAction(nameof(NewPin));
            }
        }
        catch (ReservationValidationException error) { ModelState.AddModelError("", error.Message); }
        return View(await PinCodeModelAsync(context, ct));
    }

    [AllowAnonymous, HttpGet("pin/new"), DisableRateLimiting]
    public async Task<IActionResult> NewPin(CancellationToken ct)
    {
        if (!access.Enabled || access.IsPresentation) { return NotFound(); }
        if (await DesktopAsync() is null) { return DesktopRequired(); }
        var context = await ReadPinContextAsync();
        return context is null || !await HasPinProofAsync(context, ct)
            ? RedirectToAction(nameof(Setup)) : View("Setup", new StaffPinSetupForm());
    }

    [AllowAnonymous, HttpPost("pin/new")]
    public async Task<IActionResult> NewPin(StaffPinSetupForm form, CancellationToken ct)
    {
        if (!access.Enabled || access.IsPresentation) { return NotFound(); }
        var desktop = await DesktopAsync();
        if (desktop is null) { return DesktopRequired(); }
        var context = await ReadPinContextAsync();
        if (context is null || !await HasPinProofAsync(context, ct)) { return RedirectToAction(nameof(Setup)); }
        if (!ModelState.IsValid) { return View("Setup", form); }
        if (!await pins.TakeDeviceAttemptAsync(desktop, ct)) { return RateLimited("Setup", form); }
        if (form.Pin.Distinct().Count() == 1 || "0123456789".Contains(form.Pin) || "9876543210".Contains(form.Pin))
        {
            ModelState.AddModelError(nameof(form.Pin), "Choose a PIN without repeated digits or a simple sequence.");
            return View("Setup", form);
        }
        var enrollmentVersion = await pins.ReadEnrollmentVersionAsync(ct);
        if (enrollmentVersion is null)
        {
            Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            ModelState.AddModelError("", "PIN setup is temporarily unavailable. Contact your administrator.");
            return View("Setup", form);
        }
        if (await FindEmployeeAsync(form.Pin, ct, context.UserId) is not null)
        {
            ModelState.AddModelError(nameof(form.Pin), "That PIN is unavailable. Choose a different PIN.");
            return View("Setup", form);
        }
        var employee = (await users.FindByIdAsync(context.UserId))!;
        var hash = hasher.HashPassword(employee, access.PinInput(employee, form.Pin));
        if (await pins.TrySaveVerifiedAsync(employee.Id, hash, context.Stamp, SetupScope(context), context.Email, enrollmentVersion.Value, ct))
        {
            Response.Cookies.Delete(SetupCookie, new CookieOptions { Path = "/", Secure = true });
            await HttpContext.SignOutAsync(StaffPinAccess.Scheme);
            logger.LogInformation("Staff PIN set after email verification for {EmployeeId}.", employee.Id);
            TempData["StaffPinMessage"] = "PIN saved. Sign in with your new PIN.";
            return RedirectToAction(nameof(Index));
        }
        ModelState.AddModelError("", "The PIN could not be saved. Try again, or restart PIN setup if your code expired or was already used.");
        return View("Setup", form);
    }

    private async Task<PinSetupContext?> ReadPinContextAsync()
    {
        var value = Request.Cookies[SetupCookie];
        if (string.IsNullOrEmpty(value) || value.Length > 8192) { return null; }
        try
        {
            var context = JsonSerializer.Deserialize<PinSetupContext>(protection.CreateProtector("StaffPinSetupSession").Unprotect(value));
            if (context is null || context.CreatedUtc > clock.GetUtcNow() || clock.GetUtcNow() - context.CreatedUtc >= TimeSpan.FromMinutes(30)
                || context.Desktop != await DesktopAsync()) { return null; }
            var employee = await users.FindByIdAsync(context.UserId);
            return employee is not null && employee.Email == context.Email && employee.SecurityStamp == context.Stamp
                && StaffPinAccess.GetPinRoles(employee).Any() && !await users.IsLockedOutAsync(employee) ? context : null;
        }
        catch (Exception error) when (error is CryptographicException or JsonException) { return null; }
    }

    private async Task<bool> HasPinProofAsync(PinSetupContext context, CancellationToken ct) =>
        await verification.ReadAsync(SetupScope(context), ct) is { Verified: true } challenge
        && challenge.Email == context.Email && challenge.ExpiresUtc > clock.GetUtcNow().UtcDateTime;

    private async Task SendPinCodeAsync(PinSetupContext context, CancellationToken ct)
    {
        if (!await pins.TakeEmailAttemptAsync(context.UserId, ct)) { throw new ReservationValidationException("Too many code requests. Please try again later."); }
        var code = RandomNumberGenerator.GetInt32(100000, 1000000).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var body = "protected:" + protection.CreateProtector("BookingEmail").Protect($"Your Hole In Hub staff PIN verification code is {code}. It expires in 10 minutes. Do not share it.");
        // Reuse account-email ownership challenges with a separate browser-bound scope; account confirmation cannot consume this proof.
        await verification.IssueAsync(SetupScope(context), context.Email, EmailVerificationService.CodeHash(SetupScope(context), context.Email, code),
            body, "Verify your staff PIN change", "AccountEmail", ct);
    }

    private async Task<EmailCodeFormModel> PinCodeModelAsync(PinSetupContext context, CancellationToken ct)
    {
        var challenge = await verification.ReadAsync(SetupScope(context), ct);
        var now = clock.GetUtcNow().UtcDateTime;
        return new()
        {
            Email = context.Email, ActionUrl = "/staff/pin/verify", SubmitLabel = "Verify email",
            ExpiresUtc = challenge?.ExpiresUtc ?? now, ResendAfterUtc = challenge?.ResendAfterUtc ?? now,
            ServerNowUtc = now, Expired = challenge is null || challenge.ExpiresUtc <= now, ResendWhenExpired = true
        };
    }
}
