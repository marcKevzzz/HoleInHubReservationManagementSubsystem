using HoleInHub.Business.Accounts;
using HoleInHub.Business.Reservations;
using HoleInHubReservationManagementSubsystem.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HoleInHubReservationManagementSubsystem.Controllers;

public sealed partial class LeadershipController
{
    [Authorize(Roles = StaffRoles.VenueManagement), HttpGet("/admin/resources")]
    public async Task<IActionResult> Resources(CancellationToken ct) => View((await reservations.GetResourceDirectoryAsync(ct))
        .Select(bay => bay.HasSavedPresentation ? bay : bay with { Images = ConfiguredBayPhotos(bay.Id) }).ToArray());

    [Authorize(Roles = StaffRoles.VenueManagement), HttpGet("/admin/resources/new")]
    public IActionResult NewBay() => BayEditor(new(), true);

    [Authorize(Roles = StaffRoles.VenueManagement), HttpGet("/admin/resources/bay/{id}")]
    public async Task<IActionResult> BayDetails(string id, CancellationToken ct)
    {
        var bay = (await reservations.GetResourceDirectoryAsync(ct)).FirstOrDefault(entry => entry.Id == id);
        if (bay is null) { return NotFound(); }
        return BayEditor(new()
        {
            Id = bay.Id, Name = bay.Name, Service = bay.Service, Active = bay.Active, ExpectedRevision = bay.Revision,
            MaxPlayers = bay.MaxPlayers, MaxTotalGuests = bay.MaxTotalGuests, MaxNonPlayers = bay.MaxNonPlayers,
            Location = bay.Location, Description = bay.Description,
            Images = bay.HasSavedPresentation ? bay.Images : ConfiguredBayPhotos(id)
        }, false);
    }

    [Authorize(Roles = StaffRoles.VenueManagement), HttpPost("/admin/resources"), RequestSizeLimit(25165824), RequestFormLimits(MultipartBodyLengthLimit = 25165824)]
    public Task<IActionResult> AddBay(BayEditorForm form, CancellationToken ct) => SaveBayAsync(form, true, ct);

    [Authorize(Roles = StaffRoles.VenueManagement), HttpPost("/admin/resources/edit"), RequestSizeLimit(25165824), RequestFormLimits(MultipartBodyLengthLimit = 25165824)]
    public Task<IActionResult> EditBay(BayEditorForm form, CancellationToken ct) => SaveBayAsync(form, false, ct);

    private async Task<IActionResult> SaveBayAsync(BayEditorForm form, bool create, CancellationToken ct)
    {
        try
        {
            if (!ModelState.IsValid) { throw new ReservationValidationException("Enter a bay name, a valid ID, and whole numbers for capacity."); }
            var presentation = await form.PresentationAsync(create ? [] : ConfiguredBayPhotos(form.Id), ct);
            if (create)
            {
                await reservations.AddBayAsync(form.Id, form.Name, form.Service, form.MaxPlayers, form.MaxTotalGuests, form.MaxNonPlayers, ct, presentation, form.Active ?? true);
            }
            else if (!await reservations.EditBayAsync(form.Id, form.Name, form.Service, form.MaxPlayers, form.MaxTotalGuests,
                form.MaxNonPlayers, form.ExpectedRevision, form.CapacityAcknowledgement, ct, presentation, form.Active))
            { return NotFound(new { error = "This bay no longer exists. Reload the inventory." }); }
            new ToastNotice("success", create ? "Bay added" : "Bay updated", $"{form.Name.Trim()} has been saved.").Store(TempData);
            return ResourceSaved();
        }
        catch (ResourceCapacityWarningException ex)
        {
            if (WantsResourceJson) { return Conflict(new { error = ex.Message, acknowledgement = ex.Acknowledgement }); }
            ModelState.Remove(nameof(form.CapacityAcknowledgement));
            form.CapacityAcknowledgement = ex.Acknowledgement;
            ModelState.AddModelError("", ex.Message + " Select Save bay again to apply these limits.");
        }
        catch (Exception ex) when (ex is ReservationValidationException or ArgumentException)
        {
            if (WantsResourceJson) { return Conflict(new { error = ex.Message }); }
            ModelState.AddModelError("", ex.Message);
        }
        if (form.Uploads.Count != 0) { ModelState.AddModelError("", "Choose the new photos again before saving."); }
        form.Images = form.Images.Where(path => SafeMediaPath(path)).ToArray();
        return BayEditor(form, create);
    }

    private ViewResult BayEditor(BayEditorForm form, bool create)
    {
        var configuration = HttpContext.RequestServices.GetRequiredService<IConfiguration>();
        ViewData["GalleryVideos"] = (configuration.GetSection($"BookingMedia:Bays:{form.Id}").Get<string[]>() ?? [])
            .Where(path => SafeMediaPath(path) && (path.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".webm", StringComparison.OrdinalIgnoreCase))).Distinct().ToArray();
        ViewData["CreateBay"] = create;
        ViewData["LeadershipPage"] = nameof(Resources);
        return View("BayEditor", form);
    }

    private static bool SafeMediaPath(string? path) => path is not null && path.StartsWith("/media/", StringComparison.Ordinal)
        && !path.Contains("..") && !path.Contains('\\') && !path.Any(char.IsControl);

    private string[] ConfiguredBayPhotos(string id)
    {
        var configuration = HttpContext.RequestServices.GetRequiredService<IConfiguration>();
        return (configuration.GetSection($"BookingMedia:Bays:{id}").Get<string[]>() ?? [])
            .Where(path => SafeMediaPath(path) && !path.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)
                && !path.EndsWith(".webm", StringComparison.OrdinalIgnoreCase)).Distinct().ToArray();
    }
}
