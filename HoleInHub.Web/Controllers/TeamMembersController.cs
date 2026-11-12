using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using HoleInHub.Business.Accounts;
using HoleInHub.Web.Accounts;
using HoleInHubReservationManagementSubsystem.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace HoleInHubReservationManagementSubsystem.Controllers;

[Authorize(Policy = StaffAuthorization.Workspace, Roles = StaffRoles.VenueManagement)]
[AutoValidateAntiforgeryToken, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("staff/people")]
public sealed class TeamMembersController(AccountService accounts, UserManager<AccountUser> users,
    IPasswordHasher<AccountUser> hasher, IDataProtectionProvider protection, IOptions<AccountEmailOptions> emailOptions,
    IHostEnvironment environment, StaffPinAccess access) : Controller
{
    [HttpGet("new")]
    public IActionResult Create() => access.IsPresentation ? NotFound() : CreateView(new TeamMemberForm());

    [HttpPost("new")]
    public async Task<IActionResult> Create(TeamMemberForm form, CancellationToken ct)
    {
        if (access.IsPresentation) { return NotFound(); }
        var actorRole = User.IsInRole(StaffRoles.Admin) ? StaffRoles.Admin : StaffRoles.Manager;
        if (!StaffRoles.CanCreate(actorRole, form.Role)) { return Forbid(); }
        if (!ModelState.IsValid) { return CreateView(form); }
        if (!Uri.TryCreate(emailOptions.Value.PublicBaseUrl, UriKind.Absolute, out var baseUri)
            || baseUri.Scheme is not ("https" or "http") || !environment.IsDevelopment() && baseUri.Scheme != "https")
        {
            ModelState.AddModelError("", "Configure the account email address before inviting a team member.");
            return CreateView(form);
        }
        var user = new AccountUser { Id = form.CommandId, Email = form.Email.Trim(), FirstName = form.FirstName.Trim(), LastName = form.LastName.Trim(), Phone = form.Phone.Trim() };
        user.NormalizedEmail = users.NormalizeEmail(user.Email)!;
        user.PasswordHash = hasher.HashPassword(user, Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));
        var invitation = await InvitationAsync(user, baseUri);
        var result = await accounts.CreateTeamMemberAsync(user.ToDetails(), form.Role,
            User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "", User.FindFirstValue(StaffPinAccess.SecurityStampClaim) ?? "",
            invitation, ct);
        if (result == TeamCreationResult.Forbidden) { return Forbid(); }
        if (result == TeamCreationResult.Created)
        {
            new ToastNotice("success", "Team member added", "Activation instructions queued for delivery.").Store(TempData);
            if (Request.Headers["X-People-Panel"] == "true") { return Json(new { redirectUrl = Url.Action("Person", "Leadership", new { id = user.Id }) }); }
            return RedirectToAction("Person", "Leadership", new { id = user.Id });
        }
        ModelState.AddModelError("", result == TeamCreationResult.Duplicate
            ? "This email is already registered. Open the existing account to manage access."
            : "The account could not be created. Refresh and try again.");
        return CreateView(form);
    }

    [HttpPost("{id}/invite")]
    public async Task<IActionResult> Resend(string id, string commandId, CancellationToken ct)
    {
        if (access.IsPresentation) { return NotFound(); }
        var user = AccountUser.From(await accounts.FindByIdAsync(id, ct));
        if (user is null) { return NotFound(); }
        var roles = user.Roles.Where(StaffRoles.Workspace.Contains).ToArray();
        var role = roles.Length == 1 ? roles[0] : null;
        if (role is null || !StaffRoles.CanCreate(User.IsInRole(StaffRoles.Admin) ? StaffRoles.Admin : StaffRoles.Manager, role)) { return Forbid(); }
        if (user.EmailConfirmed) { return BadRequest("This account is already active."); }
        if (!Uri.TryCreate(emailOptions.Value.PublicBaseUrl, UriKind.Absolute, out var baseUri)
            || baseUri.Scheme is not ("https" or "http") || !environment.IsDevelopment() && baseUri.Scheme != "https") { return BadRequest("Configure the account email address."); }
        var result = await accounts.CreateTeamMemberAsync(user.ToDetails(), role,
            User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "", User.FindFirstValue(StaffPinAccess.SecurityStampClaim) ?? "",
            await InvitationAsync(user, baseUri), ct, commandId);
        if (result == TeamCreationResult.Forbidden) { return Forbid(); }
        new ToastNotice(result == TeamCreationResult.Created ? "success" : "warning",
            result == TeamCreationResult.Created ? "Activation email queued" : "Invitation could not be sent").Store(TempData);
        return RedirectToAction("Person", "Leadership", new { id });
    }

    private IActionResult CreateView(TeamMemberForm form) => Request.Headers["X-People-Panel"] == "true"
        ? PartialView("_CreateForm", form) : View("Create", form);

    private async Task<string> InvitationAsync(AccountUser user, Uri baseUri)
    {
        var token = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(await users.GeneratePasswordResetTokenAsync(user)));
        var link = QueryHelpers.AddQueryString(new Uri(baseUri, "/account/activate").AbsoluteUri,
            new Dictionary<string, string?> { ["userId"] = user.Id, ["token"] = token });
        var message = "Set your password and verify your email within two hours:\n\n" + link
            + "\n\nStaff and Managers then set up their PIN on the registered staff desktop. Admins sign in with their email and password.";
        return "protected:" + protection.CreateProtector("BookingEmail").Protect(message);
    }
}
