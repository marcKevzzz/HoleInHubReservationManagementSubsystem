using System.Globalization;
using System.Security.Claims;
using HoleInHub.Business.Accounts;
using HoleInHub.Web.Accounts;
using HoleInHub.Web.ViewModels;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace HoleInHubReservationManagementSubsystem.Controllers;

[Route("staff"), AutoValidateAntiforgeryToken, EnableRateLimiting("accounts")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class StaffAccessController(StaffPinAccess access, StaffPinService pins, UserManager<AccountUser> users,
    SignInManager<AccountUser> signIn, IPasswordHasher<AccountUser> hasher, TimeProvider clock, ILogger<StaffAccessController> logger) : Controller
{
    [AllowAnonymous, HttpGet("sign-in")]
    public async Task<IActionResult> Index()
    {
        if (!access.Enabled) { return PinUnavailable(); }
        if (await DesktopAsync() is null) { return DesktopRequired(); }
        PrepareSignIn();
        return View(new StaffPinForm());
    }

    [AllowAnonymous, HttpPost("sign-in")]
    public async Task<IActionResult> Index(StaffPinForm form, CancellationToken ct)
    {
        if (!access.Enabled) { return PinUnavailable(); }
        var desktop = await DesktopAsync();
        if (desktop is null) { return DesktopRequired(); }
        PrepareSignIn();
        var email = access.IsPresentation ? null : form.Email?.Trim();
        if (access.IsPresentation) { ModelState.Remove(nameof(form.Email)); }
        if (!access.IsPresentation && string.IsNullOrEmpty(email))
        {
            ModelState.AddModelError(nameof(form.Email), "Enter your work email.");
        }
        if (!ModelState.IsValid) { return View(form); }
        if (!await pins.TakeDeviceAttemptAsync(desktop, ct)) { return RateLimited("Index", form); }

        var employee = access.IsPresentation ? await FindPresentationEmployeeAsync(form.Pin, ct) : await users.FindByEmailAsync(email!);
        var roles = access.GetSignInRoles(employee).ToArray();
        if (employee is not null && roles.Length > 0)
        {
            if (!await pins.TakeEmployeeAttemptAsync(employee.Id, ct)) { return RateLimited("Index", form); }
            var credential = await pins.ReadAsync(employee.Id, ct);
            if (credential is not null && !await users.IsLockedOutAsync(employee)
                && hasher.VerifyHashedPassword(employee, credential.Hash, access.PinInput(employee, form.Pin)) != PasswordVerificationResult.Failed)
            {
                var claims = new List<Claim>
                {
                    new Claim(ClaimTypes.NameIdentifier, employee.Id),
                    new Claim(ClaimTypes.Name, employee.Email),
                    new Claim(StaffPinAccess.StaffHostClaim, desktop),
                    new Claim(StaffPinAccess.PinVersionClaim, credential.Version),
                    new Claim(StaffPinAccess.SecurityStampClaim, employee.SecurityStamp),
                    new Claim(StaffPinAccess.StartedClaim, clock.GetUtcNow().ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture))
                };
                claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));
                await HttpContext.SignInAsync(StaffPinAccess.Scheme, new ClaimsPrincipal(new ClaimsIdentity(claims, StaffPinAccess.Scheme)));
                logger.LogInformation("Staff PIN sign-in succeeded for {EmployeeId}.", employee.Id);
                if (roles.Contains(StaffRoles.Admin)) { return RedirectToAction("Admin", "Leadership"); }
                if (roles.Contains(StaffRoles.Manager)) { return RedirectToAction("Manager", "Leadership"); }
                return RedirectToAction("Index", "BookingReview");
            }
        }
        logger.LogWarning("Staff PIN sign-in rejected for {EmployeeId}.", employee?.Id);
        ModelState.AddModelError("", access.IsPresentation
            ? "That PIN didn’t work. Enter it again or contact your administrator."
            : "Unable to sign in. Check your work email and PIN, or contact your administrator.");
        return View(form);
    }

    [AllowAnonymous, HttpGet("pin/setup")]
    public async Task<IActionResult> Setup()
    {
        if (!access.Enabled || access.IsPresentation) { return NotFound(); }
        if (await DesktopAsync() is null) { return DesktopRequired(); }
        return View(new StaffPinSetupForm());
    }

    [AllowAnonymous, HttpPost("pin/setup")]
    public async Task<IActionResult> Setup(StaffPinSetupForm form, CancellationToken ct)
    {
        if (!access.Enabled || access.IsPresentation) { return NotFound(); }
        var desktop = await DesktopAsync();
        if (desktop is null) { return DesktopRequired(); }
        if (!ModelState.IsValid) { return View(form); }
        if (!await pins.TakeDeviceAttemptAsync(desktop, ct)) { return RateLimited("Setup", form); }

        var employee = await users.FindByEmailAsync(form.Email.Trim());
        if (employee is not null && StaffPinAccess.GetWorkspaceRoles(employee).Any() &&
            (await signIn.CheckPasswordSignInAsync(employee, form.Password, lockoutOnFailure: true)).Succeeded)
        {
            if (form.Pin.Distinct().Count() == 1 || "0123456789".Contains(form.Pin) || "9876543210".Contains(form.Pin))
            {
                ModelState.AddModelError(nameof(form.Pin), "Choose a PIN without repeated digits or a simple sequence.");
                return View(form);
            }
            await pins.SaveAsync(employee.Id, hasher.HashPassword(employee, access.PinInput(employee, form.Pin)), ct);
            await HttpContext.SignOutAsync(StaffPinAccess.Scheme);
            logger.LogInformation("Staff PIN set for {EmployeeId}.", employee.Id);
            TempData["StaffPinMessage"] = "PIN saved. Sign in with your work email and new PIN.";
            return RedirectToAction(nameof(Index));
        }
        ModelState.AddModelError("", "Unable to set a PIN. Check your work email and password, or contact your administrator.");
        return View(form);
    }

    [Authorize(Policy = StaffAuthorization.Workspace), HttpPost("sign-out"), HttpPost("switch")]
    public async Task<IActionResult> SignOutStaff()
    {
        logger.LogInformation("Staff PIN session ended for {EmployeeId}.", User.FindFirstValue(ClaimTypes.NameIdentifier));
        await HttpContext.SignOutAsync(StaffPinAccess.Scheme);
        return RedirectToAction(nameof(Index));
    }

    private Task<string?> DesktopAsync() => access.GetDesktopAsync(HttpContext, clock.GetUtcNow());

    private void PrepareSignIn()
    {
        ViewData["Presentation"] = access.IsPresentation;
    }

    private async Task<AccountUser?> FindPresentationEmployeeAsync(string pin, CancellationToken ct)
    {
        AccountUser? match = null;
        foreach (var email in new[] { access.PresentationAccount, access.PresentationManagerAccount, access.PresentationAdminAccount }
            .Where(email => !string.IsNullOrWhiteSpace(email)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var employee = await users.FindByEmailAsync(email);
            if (employee is null || !access.GetSignInRoles(employee).Any()) { continue; }
            var credential = await pins.ReadAsync(employee.Id, ct);
            if (credential is null || hasher.VerifyHashedPassword(employee, credential.Hash, access.PinInput(employee, pin)) == PasswordVerificationResult.Failed) { continue; }
            if (match is not null) { return null; }
            match = employee;
        }
        return match;
    }

    private IActionResult PinUnavailable()
    {
        Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        return View("Unavailable");
    }

    private IActionResult DesktopRequired()
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        return View("DesktopRequired");
    }

    private IActionResult RateLimited(string view, object form)
    {
        Response.StatusCode = StatusCodes.Status429TooManyRequests;
        ModelState.AddModelError("", "Too many attempts. Wait up to 15 minutes before trying again.");
        return View(view, form);
    }
}
