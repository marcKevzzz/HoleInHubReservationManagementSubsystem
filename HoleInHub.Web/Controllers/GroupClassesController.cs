using System.Security.Claims;
using HoleInHub.Business.Accounts;
using HoleInHub.Business.Reservations;
using HoleInHub.Web.Accounts;
using HoleInHubReservationManagementSubsystem.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HoleInHubReservationManagementSubsystem.Controllers;

[Authorize(Policy = StaffAuthorization.Workspace), Route("staff/group-classes"), ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class GroupClassesController(ReservationService bookings, TimeProvider clock) : Controller
{
    [Authorize(Roles = StaffRoles.VenueManagement), HttpGet("/admin/services/{offering}/packages")]
    public async Task<IActionResult> Packages(string offering, CancellationToken ct, string? editing = null)
    {
        var editor = await bookings.PackageEditorAsync(offering, ct);
        if (editor == null) { return NotFound(); }
        var selected = editor.Sessions.FirstOrDefault(x => x.Id == editing);
        if (editing != null && selected == null) { return NotFound(); }
        ViewData["ClassDate"] = new PublishGroupClassForm
        {
            Offering = offering,
            Editing = editing,
            Revision = selected?.Revision ?? 0,
            Date = selected == null ? null : DateOnly.FromDateTime(DashboardMetrics.Local(selected.StartUtc)),
            StartTime = selected == null ? new TimeOnly(15, 0) : TimeOnly.FromDateTime(DashboardMetrics.Local(selected.StartUtc)),
            EndTime = selected == null ? new TimeOnly(16, 0) : TimeOnly.FromDateTime(DashboardMetrics.Local(selected.EndUtc)),
            Capacity = selected?.Capacity,
            BayIds = selected?.Bays ?? [],

            Bays = (await bookings.GetCatalogAsync(ct)).Where(bay => bay.Service == "Regular").ToArray()
        };
        return View(editor);
    }

    [Authorize(Roles = StaffRoles.VenueManagement), HttpPost("/admin/services/{offering}/packages"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Packages(string offering, string[] sessions, DateOnly? deadline, CancellationToken ct)
    {
        if (deadline is null || deadline.Value.Year >= 9999)
        { ModelState.AddModelError("deadline", "Enter a registration deadline."); return await Packages(offering, ct); }
        try { await bookings.PublishPackageAsync(offering, sessions, deadline.Value, ct); }
        catch (ReservationValidationException ex)
        {
            ModelState.AddModelError("", ex.Message);
            return await Packages(offering, ct);
        }
        return RedirectToAction(nameof(Packages), new { offering });
    }

    [Authorize(Roles = StaffRoles.VenueManagement), HttpPost("/admin/services/{offering}/packages/{id}/close"), ValidateAntiForgeryToken]
    public async Task<IActionResult> ClosePackage(string offering, string id, CancellationToken ct)
    {
        if (!await bookings.ClosePackageAsync(offering, id, ct)) { return NotFound(); }
        return RedirectToAction(nameof(Packages), new { offering });
    }

    [HttpPost("{id}/absence"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Absence(string id, string reservationId, string learnerId, string decision, CancellationToken ct)
    {
        if (decision is not ("Approve" or "Decline") || !await bookings.UpdatePackageSeatAsync(reservationId, id, learnerId, decision, ct)) { return BadRequest(); }
        new ToastNotice("success", "Absence request updated", decision).Store(TempData);
        return RedirectToAction(nameof(Roster), new { id });
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(DateOnly? from, CancellationToken ct)
    {
        var day = from ?? DateOnly.FromDateTime(DashboardMetrics.Local(clock.GetUtcNow().UtcDateTime));
        if (!ModelState.IsValid || day.Year is < 2000 or > 2100)
        {
            return BadRequest("Choose a valid start date.");
        }

        ViewData["From"] = day;
        return View(await bookings.StaffClassesAsync(day, ct));
    }

    [Authorize(Roles = StaffRoles.VenueManagement), HttpGet("publish")]
    public Task<IActionResult> Publish(string? offering, CancellationToken ct) => Form(new() { Offering = offering ?? "" }, ct);

    private async Task<IActionResult> Form(PublishGroupClassForm model, CancellationToken ct)
    {
        if (!bookings.SharedClassesEnabled)
        {
            return NotFound();
        }

        model.Bays = (await bookings.GetCatalogAsync(ct)).Where(x => x.Service == "Regular").ToArray();
        model.Offerings = await bookings.GroupOfferingsAsync(ct);
        return View("Publish", model);
    }

    [Authorize(Roles = StaffRoles.VenueManagement), HttpPost("publish"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Publish(PublishGroupClassForm model, CancellationToken ct, bool schedule = false)
    {
        if (!bookings.SharedClassesEnabled)
        {
            return NotFound();
        }

        if (ModelState.IsValid)
        {
            try
            {
                var id = await bookings.PublishGroupClassAsync(model.Date!.Value, model.Offering, model.BayIds, model.Capacity!.Value,
                    User.FindFirstValue(ClaimTypes.NameIdentifier)!, ct, model.StartTime, model.EndTime, model.Editing, model.Revision, model.WeeklyPackage);
                if (schedule) { return RedirectToAction(nameof(Packages), new { offering = model.Offering }); }
                return RedirectToAction(nameof(Roster), new { id });
            }
            catch (ReservationValidationException error) { ModelState.AddModelError("", error.Message); }
        }
        if (schedule && await bookings.PackageEditorAsync(model.Offering, ct) is { } editor)
        {
            model.Bays = (await bookings.GetCatalogAsync(ct)).Where(bay => bay.Service == "Regular").ToArray();
            ViewData["ClassDate"] = model;
            ViewData["ClassDateError"] = true;
            return View("Packages", editor);
        }
        return await Form(model, ct);
    }

    [HttpGet("{id}/enroll")]
    public async Task<IActionResult> Enroll(string id, CancellationToken ct)
    {
        if (!bookings.SharedClassesEnabled) { return NotFound(); }
        var offering = Request.Query["offering"].ToString();
        if (string.IsNullOrWhiteSpace(offering) || !(await bookings.FamilyClassesAsync(offering, 1, ct)).Any(package => package.ClassSessionId == id))
        { return NotFound(); }
        var request = new SubmitReservationRequest
        {
            Service = "KpgaGroup",
            ServiceEntryId = offering,
            ClassSessionId = id,
            IdempotencyKey = Guid.NewGuid().ToString("N")
        };
        return await EnrollmentForm(request, 1, ct);
    }

    private async Task<IActionResult> EnrollmentForm(SubmitReservationRequest request, int count, CancellationToken ct)
    {
        ViewData["StaffFamilyEntry"] = true;
        ViewData["LearnerCount"] = count;
        ViewData["ClassSessions"] = await bookings.FamilyClassesAsync(request.ServiceEntryId, 1, ct);
        return View("Enroll", request);
    }

    [HttpPost("enroll"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Enroll(SubmitReservationRequest request, int learnerCount, string? intent, string? source,
        string? reviewedClassSessionId, int reviewedLearnerCount, CancellationToken ct)
    {
        if (!bookings.SharedClassesEnabled || learnerCount is < 1 or > 8) { return BadRequest(); }
        request.ServiceEntryId = request.Service.Split(':').ElementAtOrDefault(1) ?? "KpgaGroup";
        request.Service = "KpgaGroup";
        ViewData["Source"] = source;
        if (intent == "familyForm" || reviewedClassSessionId != request.ClassSessionId || reviewedLearnerCount != learnerCount)
        {
            ModelState.Clear();
            return await EnrollmentForm(request, learnerCount, ct);
        }
        if (request.Learners?.Length != learnerCount) { ModelState.AddModelError("", "Review all learner details."); }
        if (ModelState.IsValid)
        {
            try
            {
                var result = await bookings.SubmitStaffFamilyAsync(request, User.FindFirstValue(ClaimTypes.NameIdentifier)!, source ?? "", null, ct);
                return RedirectToAction("Details", "BookingReview", new { id = result.Id });
            }
            catch (ReservationValidationException error) { ModelState.AddModelError("", error.Message); }
        }
        return await EnrollmentForm(request, learnerCount, ct);
    }

    [HttpGet("{id}/roster")]
    public async Task<IActionResult> Roster(string id, CancellationToken ct)
    {
        var result = await bookings.StaffClassRosterAsync(id, ct);
        return result is null ? NotFound() : View(result.Value);
    }

    [HttpPost("{id}/attendance"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Attendance(string id, string reservationId, string learnerId, string attendance, CancellationToken ct)
    {
        if (!await bookings.MarkGroupAttendanceAsync(id, reservationId, learnerId, attendance, User.FindFirstValue(ClaimTypes.NameIdentifier)!, ct))
        {
            new ToastNotice("error", "Attendance could not be updated", "Check the booking status and class time.").Store(TempData);
        }

        else { new ToastNotice("success", "Attendance updated", attendance).Store(TempData); }
        return RedirectToAction(nameof(Roster), new { id });
    }

    [Authorize(Roles = StaffRoles.VenueManagement), HttpPost("{id}/close"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Close(string id, CancellationToken ct)
    {
        var closed = await bookings.CloseGroupClassAsync(id, ct);
        new ToastNotice(closed ? "success" : "error", closed ? "Enrollment closed" : "Enrollment could not be closed").Store(TempData);
        return RedirectToAction(nameof(Roster), new { id });
    }

    [Authorize(Roles = StaffRoles.VenueManagement), HttpPost("{id}/release"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Release(string id, CancellationToken ct)
    {
        if (!await bookings.ReleaseGroupClassAsync(id, ct))
        {
            new ToastNotice("error", "Bays could not be released", "Close enrollment and resolve enrolled seats first.").Store(TempData);
        }

        else { new ToastNotice("success", "Class bays released").Store(TempData); }
        return RedirectToAction(nameof(Roster), new { id });
    }
}
