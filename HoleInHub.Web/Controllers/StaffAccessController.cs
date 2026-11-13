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
public sealed partial class StaffAccessController(StaffPinAccess access, StaffPinService pins, UserManager<AccountUser> users,
    SignInManager<AccountUser> signIn, IPasswordHasher<AccountUser> hasher, TimeProvider clock, ILogger<StaffAccessController> logger,
    EmailVerificationService verification, Microsoft.AspNetCore.DataProtection.IDataProtectionProvider protection) : Controller
{
    [AllowAnonymous, HttpGet("sign-in"), DisableRateLimiting]
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
        if (!ModelState.IsValid) { return View(form); }
        if (!await pins.TakeDeviceAttemptAsync(desktop, ct)) { return RateLimited("Index", form); }

        var employee = access.IsPresentation ? await FindPresentationEmployeeAsync(form.Pin, ct) : await FindEmployeeAsync(form.Pin, ct);
        var roles = access.GetSignInRoles(employee).ToArray();
        if (employee is not null && roles.Length > 0)
        {
            if (!await pins.TakeEmployeeAttemptAsync(employee.Id, ct)) { return RateLimited("Index", form); }
            var credential = await pins.ReadAsync(employee.Id, ct);
            if (credential is not null && !await users.IsLockedOutAsync(employee)
                && hasher.VerifyHashedPassword(employee, credential.Hash, access.PinInput(employee, form.Pin)) != PasswordVerificationResult.Failed)
            {
                await StaffPinAccess.SignInAsync(HttpContext, employee, clock.GetUtcNow(),
                [
                    new Claim(StaffPinAccess.StaffHostClaim, desktop),
                    new Claim(StaffPinAccess.PinVersionClaim, credential.Version),
                    .. roles.Select(role => new Claim(ClaimTypes.Role, role))
                ]);
                logger.LogInformation("Staff PIN sign-in succeeded for {EmployeeId}.", employee.Id);
                if (roles.Contains(StaffRoles.Manager)) { return RedirectToAction(nameof(LeadershipController.Dashboard), "Leadership"); }
                return RedirectToAction("Index", "BookingReview");
            }
        }
        logger.LogWarning("Staff PIN sign-in rejected for {EmployeeId}.", employee?.Id);
        ModelState.AddModelError("", "That PIN didn’t work. Enter it again, reset your PIN, or contact your administrator.");
        return View(form);
    }

    [Authorize(Policy = StaffAuthorization.Workspace), HttpPost("sign-out"), HttpPost("switch")]
    public async Task<IActionResult> SignOutStaff()
    {
        logger.LogInformation("Workspace session ended for {EmployeeId}.", User.FindFirstValue(ClaimTypes.NameIdentifier));
        await HttpContext.SignOutAsync(StaffPinAccess.Scheme);
        if (User.IsInRole(StaffRoles.Admin))
        {
            await signIn.SignOutAsync();
            return RedirectToAction("SignIn", "Account");
        }
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
        foreach (var email in new[] { access.PresentationAccount, access.PresentationManagerAccount }
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

    private async Task<AccountUser?> FindEmployeeAsync(string pin, CancellationToken ct, string? excludeId = null)
    {
        AccountUser? match = null;
        // shortcut: checks existing salted PIN hashes for this small venue; add an indexed lookup if the staff roster grows.
        foreach (var candidate in await pins.ReadCandidatesAsync(ct))
        {
            if (candidate.AccountId == excludeId) { continue; }
            var employee = await users.FindByIdAsync(candidate.AccountId);
            if (employee is null || !access.GetSignInRoles(employee).Any()) { continue; }
            if (hasher.VerifyHashedPassword(employee, candidate.Hash, access.PinInput(employee, pin)) == PasswordVerificationResult.Failed) { continue; }
            if (excludeId is not null) { return employee; }
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
