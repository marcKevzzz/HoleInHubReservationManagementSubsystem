using System.Security.Claims;
using HoleInHub.Business.Reservations;
using HoleInHub.Web.Accounts;
using HoleInHubReservationManagementSubsystem.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HoleInHubReservationManagementSubsystem.Controllers;

[Authorize(Policy = StaffAuthorization.Workspace), Route("staff/coaches"), ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class CoachesController(ReservationService bookings) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct) => View(await bookings.CoachDirectoryAsync(ct));

    [HttpGet("new")]
    public IActionResult Create() => View("Edit", CoachEditorForm.From(null));

    [HttpGet("{id}/edit")]
    public async Task<IActionResult> Edit(string id, CancellationToken ct)
    {
        var coach = (await bookings.CoachesAsync(ct)).FirstOrDefault(x => x.Id == id);
        return coach is null ? NotFound() : View(CoachEditorForm.From(coach));
    }

    [HttpPost("save"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(CoachEditorForm form, string? intent, CancellationToken ct)
    {
        if (intent is not null)
        {
            var focus = ChangeRows(form, intent);
            if (focus is null) { return BadRequest(); }
            if (intent.StartsWith("remove-", StringComparison.Ordinal))
            { new ToastNotice("success", "Row removed", "Save the coach to apply your changes.").Store(TempData); }
            ViewData["RowFocus"] = focus;
            return View("Edit", form);
        }
        foreach (var error in form.ValidateSchedule())
        {
            ModelState.AddModelError(error.MemberNames.Single(), error.ErrorMessage!);
        }

        if (ModelState.IsValid)
        {
            var coach = new CoachDto(form.Id ?? Guid.NewGuid().ToString("N"), form.Name.Trim(), form.Active, form.Revision ?? 0,
                form.Weekly.Where(x => x.Start.HasValue).Select(x => new CoachWeeklyHoursDto(x.Day, Minute(x.Start)!.Value, Minute(x.End)!.Value)).ToArray(),
                form.Exceptions.Where(x => x.Date.HasValue).Select(x => new CoachDateHoursDto(x.Date!.Value.ToString("yyyy-MM-dd"), Minute(x.Start), Minute(x.End))).ToArray(), form.Phone, form.Email, form.Title);
            try
            {
                if (await bookings.SaveCoachAsync(coach, form.Revision, ct))
                {
                    new ToastNotice("success", form.Revision.HasValue ? "Coach updated" : "Coach added", coach.Name).Store(TempData);
                    return RedirectToAction(nameof(Index));
                }

                ViewData["StaleProfile"] = true;
                ModelState.AddModelError("", "Another staff member changed this profile. Reopen it to review their changes.");
            }
            catch (CoachCommitmentException error) { ViewData["Conflicts"] = error.Bookings; ModelState.AddModelError("", error.Message); }
            catch (ReservationValidationException error) { ModelState.AddModelError("", error.Message); }
        }
        return View("Edit", form);
    }

    private string? ChangeRows(CoachEditorForm form, string intent)
    {
        if (intent == "add-week") { form.Weekly.Add(new()); return $"Weekly[{form.Weekly.Count - 1}].Day"; }
        if (intent == "add-date") { form.Exceptions.Add(new()); return $"Exceptions[{form.Exceptions.Count - 1}].Date"; }

        var parts = intent.Split(':', 2);
        if (parts.Length != 2 || !int.TryParse(parts[1], out var index) || index < 0) { return null; }
        var weekly = parts[0] == "remove-week";
        var count = weekly ? form.Weekly.Count : form.Exceptions.Count;
        if ((!weekly && parts[0] != "remove-date") || index >= count) { return null; }
        if (weekly) { form.Weekly.RemoveAt(index); }
        else { form.Exceptions.RemoveAt(index); }

        // Tag helpers prefer attempted values. Shift those with their rows, including invalid input.
        var collection = weekly ? "Weekly" : "Exceptions";
        for (var row = index; row < count; row++)
        {
            var prefix = $"{collection}[{row}].";
            foreach (var key in ModelState.Keys.Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToArray())
            {
                var entry = ModelState[key]!;
                if (row > index)
                {
                    ModelState.SetModelValue($"{collection}[{row - 1}].{key[prefix.Length..]}", entry.RawValue, entry.AttemptedValue);
                }
                ModelState.Remove(key);
            }
        }
        return count == 1 ? (weekly ? "add-week" : "add-date")
            : $"{collection}[{Math.Min(index, count - 2)}].{(weekly ? "Day" : "Date")}";
    }

    private static int? Minute(TimeOnly? time) => time.HasValue ? time.Value.Hour * 60 + time.Value.Minute : null;

    [HttpGet("bookings/{id}/assignment")]
    public async Task<IActionResult> Assignment(string id, CancellationToken ct)
    {
        var booking = await bookings.GetStaffAsync(id, ct);
        if (booking is null || booking.Service != "PhilippineCoach")
        {
            return NotFound();
        }

        ViewData["Booking"] = booking;
        return View(await bookings.AvailableCoachesAsync(id, ct));
    }

    [HttpPost("bookings/{id}/assignment"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Assignment(string id, long revision, string coachId, CancellationToken ct)
    {
        try
        {
            if (await bookings.AssignCoachAsync(id, revision, coachId, User.FindFirstValue(ClaimTypes.NameIdentifier)!, ct))
            {
                new ToastNotice("success", "Coach assignment saved").Store(TempData);
                return RedirectToAction("Details", "BookingReview", new { id });
            }

            ModelState.AddModelError("", "The booking or coach schedule changed. Review the available coaches.");
        }
        catch (ReservationValidationException error) { ModelState.AddModelError("", error.Message); }
        ViewData["AttemptedCoachId"] = coachId;
        return await Assignment(id, ct);
    }
}
