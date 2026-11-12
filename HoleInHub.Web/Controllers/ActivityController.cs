using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HoleInHub.Business.Accounts;
using HoleInHub.Business.Activity;
using HoleInHub.Business.Customers;
using HoleInHub.Business.Reservations;
using HoleInHub.Web.Accounts;
using HoleInHubReservationManagementSubsystem.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
namespace HoleInHubReservationManagementSubsystem.Controllers;

[Authorize(Policy = StaffAuthorization.Workspace), ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("staff/activity")]
public sealed class ActivityController(ActivityService activity, IDataProtectionProvider protection, TimeProvider clock,
    ReservationService reservations, CustomerProfileService profiles, AccountService accounts) : Controller
{
    private bool Access => User.IsInRole(StaffRoles.Admin);
    private sealed record CursorEnvelope(string Cursor, string Context);
    private string Context(ActivityQuery q) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { q.From, q.To, q.Search, q.Category, q.Actor, q.AsOf, Access, User = User.FindFirstValue(ClaimTypes.NameIdentifier) }))));
    private void Dates(ActivityQuery q)
    {
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), ReservationRules.Manila).DateTime);
        if (q.Range != "custom" && (q.AsOf is null || q.From is null || q.To is null)) { q.To = today; q.From = today.AddDays(q.Range switch { "today" => 0, "month" => -29, _ => -6 }); }
        q.Search = (q.Search ?? "").Trim(); q.Actor = string.IsNullOrWhiteSpace(q.Actor) ? "everyone" : q.Actor;
        q.Category = new[] { "All", "Reservations", "Customers", "Schedule", "Access" }
            .FirstOrDefault(category => category.Equals(q.Category, StringComparison.OrdinalIgnoreCase)) ?? q.Category ?? "All";
    }
    [HttpGet("")]
    public async Task<IActionResult> Index([FromQuery] ActivityQuery q, CancellationToken ct)
    {
        Dates(q);
        if (q.Category.Equals("Access", StringComparison.OrdinalIgnoreCase) && !Access) { return StatusCode(StatusCodes.Status403Forbidden); }
        ActivityPage? page = null; string? error = null;
        try
        {
            if (!ModelState.IsValid || q.From is null || q.To is null) { throw new ArgumentException("Choose a valid date range."); }
            string? cursor = null;
            if (q.Cursor is not null)
            {
                if (q.Cursor.Length > 8000) { throw new ArgumentException("This page link is invalid. Refresh activity."); }
                var data = JsonSerializer.Deserialize<CursorEnvelope>(protection.CreateProtector("activity-cursor-v1").Unprotect(q.Cursor));
                if (data is null || data.Context != Context(q)) { throw new ArgumentException("Filters changed. Refresh activity."); }
                cursor = data.Cursor;
            }
            page = await activity.QueryAsync(new(q.From.Value, q.To.Value, q.Search, q.Category, q.Actor, cursor, q.AsOf), Access, User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "", ct);
            q.AsOf = page.AsOfUtc;
            if (page.NextCursor is not null) { page = page with { NextCursor = protection.CreateProtector("activity-cursor-v1").Protect(JsonSerializer.Serialize(new CursorEnvelope(page.NextCursor, Context(q)))) }; }
        }
        catch (UnauthorizedAccessException) { return StatusCode(StatusCodes.Status403Forbidden); }
        catch (Exception ex) when (ex is ArgumentException or CryptographicException or JsonException) { error = ex is CryptographicException or JsonException ? "This page link expired. Refresh activity." : ex.Message; Response.StatusCode = 400; }
        var model = new ActivityListModel(q, page, Access, error);
        return Request.Headers["X-Activity"] == "list" ? PartialView("_Results", model) : View(model);
    }
    [HttpGet("{key}")]
    public async Task<IActionResult> Details(string key, [FromQuery] ActivityQuery q, CancellationToken ct)
    {
        if (key.Length > 1500) { return NotFound(); }
        ActivityEntry? entry;
        try { entry = await activity.GetAsync(key, Access, ct); } catch (UnauthorizedAccessException) { return StatusCode(StatusCodes.Status403Forbidden); } catch (ArgumentException) { return NotFound(); }
        if (entry is null) { return NotFound(); }
        string? target = null; var action = "Open record"; var id = entry.TargetId;
        if (entry.TargetKind == "reservation" && await reservations.StaffRecordExistsAsync(id, ct)) { target = Url.Action("Details", "BookingReview", new { id, date = entry.TargetDate?.ToString("yyyy-MM-dd") }); action = "Open booking"; }
        else if (entry.TargetKind == "customer" && await profiles.GetAsync(id, ct) is not null) { target = Url.Action("Details", "Customers", new { id }); action = "View customer"; }
        else if (entry.TargetKind == "customer-merge" && await profiles.GetAsync(id, ct) is not null) { target = Url.Action("MergeHistory", "Customers", new { id }); action = "View merge history"; }
        else if (entry.TargetKind == "account" && User.ManagesVenue() && await accounts.FindByIdAsync(id, ct) is not null) { target = Url.Action("Person", "Leadership", new { id }); action = "View team member"; }
        else if (entry.TargetKind == "schedule" && await reservations.GetScheduleBlockAsync(id, ct) is { } block)
        {
            if (User.ManagesVenue())
            {
                target = Url.Action("Block", "BookingReview", new { id });
                action = "View schedule block";
            }
            else if (block.ReleasedUtc is null && entry.TargetDate is { } day)
            {
                target = Url.Action("Index", "BookingReview", new { date = day.ToString("yyyy-MM-dd"), activityBlock = block.Id });
                action = "View schedule";
            }
        }
        var back = QueryHelpers.AddQueryString(q.Url(), "focus", entry.Key);
        if (target is not null) { target = QueryHelpers.AddQueryString(target, "activityReturn", back); }
        var model = new ActivityDetailModel(entry, back, target, action);
        return Request.Headers["X-Activity"] == "panel" ? PartialView("_Event", model) : View(model);
    }
}
