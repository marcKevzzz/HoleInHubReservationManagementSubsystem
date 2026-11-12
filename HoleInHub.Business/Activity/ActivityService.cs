using System.Globalization;
using System.Text;
using System.Text.Json;
using HoleInHub.Data.Activity;

namespace HoleInHub.Business.Activity;

public sealed record ActivityFilter(DateOnly From, DateOnly To, string? Search = null, string Category = "All",
    string Actor = "everyone", string? Cursor = null, DateTime? AsOfUtc = null);
public sealed record ActivityChange(string Field, string? Before, string? After);
public sealed record ActivityEntry(string Key, DateTime OccurredUtc, string Category, string Title, string Actor,
    string? ActorId, string TargetLabel, string TargetKind, string TargetId, DateOnly? TargetDate,
    string? Reason, IReadOnlyList<ActivityChange> Changes);
public sealed record ActivityPage(IReadOnlyList<ActivityEntry> Entries, string? NextCursor, DateTime AsOfUtc);

public sealed class ActivityService(ActivityPersistence persistence, TimeProvider clock)
{
    private static readonly TimeZoneInfo Manila = TimeZoneInfo.FindSystemTimeZoneById("Asia/Manila");
    private static readonly string[] Categories = ["All", "Reservations", "Customers", "Schedule", "Access"];

    public async Task<ActivityPage> QueryAsync(ActivityFilter filter, bool includeAccess, string actorId, CancellationToken ct)
    {
        var query = Validate(filter, includeAccess, actorId, clock.GetUtcNow().UtcDateTime);
        var rows = await persistence.QueryAsync(query, includeAccess, ct);
        var entries = rows.Take(50).Select(Map).ToArray();
        var next = rows.Count > 50 ? Encode(JsonSerializer.Serialize(new ActivityPosition(rows[49].OccurredUtc, rows[49].Key))) : null;
        return new(entries, next, query.AsOfUtc);
    }

    public async Task<ActivityEntry?> GetAsync(string key, bool includeAccess, CancellationToken ct)
    {
        string identity;
        try { identity = Decode(key); }
        catch (ArgumentException) { return null; }
        var row = await persistence.GetAsync(identity, includeAccess, ct);
        return row is null || row.OccurredUtc == default ? null : Map(row);
    }

    public static ActivityQuery Validate(ActivityFilter filter, bool includeAccess, string actorId, DateTime nowUtc)
    {
        if (filter.To < filter.From || filter.To.DayNumber - filter.From.DayNumber >= 90 || filter.To == DateOnly.MaxValue)
        { throw new ArgumentException("Choose an inclusive date range of up to 90 days."); }
        if (!Categories.Contains(filter.Category, StringComparer.Ordinal)) { throw new ArgumentException("Choose a valid activity category."); }
        if (filter.Category == "Access" && !includeAccess) { throw new UnauthorizedAccessException("Access activity is restricted to Admin."); }
        var search = filter.Search?.Trim();
        if (search?.Length > 120) { throw new ArgumentException("Search must be 120 characters or fewer."); }
        if (filter.Actor.Length > 200 || filter.Actor.Any(char.IsControl)) { throw new ArgumentException("Choose a valid actor."); }
        var actor = filter.Actor switch { "everyone" => null, "mine" => actorId, _ => filter.Actor };
        if (actor is not null && string.IsNullOrWhiteSpace(actor)) { throw new ArgumentException("Choose a valid actor."); }
        var from = TimeZoneInfo.ConvertTimeToUtc(filter.From.ToDateTime(TimeOnly.MinValue), Manila);
        var to = TimeZoneInfo.ConvertTimeToUtc(filter.To.AddDays(1).ToDateTime(TimeOnly.MinValue), Manila);
        var asOf = filter.AsOfUtc ?? nowUtc;
        if (asOf.Kind != DateTimeKind.Utc || asOf > nowUtc.AddMinutes(1)) { throw new ArgumentException("The activity paging session is invalid. Refresh activity."); }
        // BSON dates have millisecond precision; use that same precision throughout a paging session.
        asOf = new DateTime(asOf.Ticks - asOf.Ticks % TimeSpan.TicksPerMillisecond, DateTimeKind.Utc);
        ActivityPosition? after = null;
        if (!string.IsNullOrEmpty(filter.Cursor))
        {
            try { after = JsonSerializer.Deserialize<ActivityPosition>(Decode(filter.Cursor)); }
            catch (JsonException) { throw new ArgumentException("The activity cursor is invalid. Refresh activity."); }
            if (after is null || string.IsNullOrWhiteSpace(after.Key) || after.Key.Length > 500 || after.Key.Split('|').Length != 3 ||
                after.OccurredUtc.Kind != DateTimeKind.Utc || after.OccurredUtc < from || after.OccurredUtc >= to || after.OccurredUtc > asOf || filter.AsOfUtc is null)
            { throw new ArgumentException("The activity cursor is invalid. Refresh activity."); }
        }
        return new(from, to, asOf, filter.Category == "All" ? null : filter.Category, actor, search, after);
    }

    private static string Encode(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static string Decode(string value)
    {
        if (value.Length > 2000 || value.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-' && c != '_')) { throw new ArgumentException("Invalid activity key or cursor."); }
        try { return Encoding.UTF8.GetString(Convert.FromBase64String(value.Replace('-', '+').Replace('_', '/').PadRight((value.Length + 3) / 4 * 4, '='))); }
        catch (FormatException) { throw new ArgumentException("Invalid activity key or cursor."); }
    }

    private static ActivityEntry Map(ActivityRecord row)
    {
        var changes = new List<ActivityChange>();
        void Add(string field, string? before, string? after)
        {
            if (before != after && (!string.IsNullOrEmpty(before) || !string.IsNullOrEmpty(after))) { changes.Add(new(field, before, after)); }
        }
        static string? Local(DateTime? time) => time.HasValue ? TimeZoneInfo.ConvertTimeFromUtc(time.Value, Manila).ToString("d MMM yyyy, HH:mm", CultureInfo.InvariantCulture) : null;
        Add("Status", row.PreviousStatus, row.Status);
        Add("Start", Local(row.BeforeStartUtc), Local(row.AfterStartUtc));
        Add("End", Local(row.BeforeEndUtc), Local(row.AfterEndUtc));
        Add("Resources", string.Join(", ", row.BeforeResources), string.Join(", ", row.AfterResources));
        Add("Roles", string.Join(", ", row.PreviousRoles), string.Join(", ", row.NewRoles));
        if (row.Category == "Customers")
        {
            Add("Source customer", null, row.SourceName);
            Add("Target customer", null, row.TargetName);
            Add("Reservations", null, row.ReservationCount?.ToString(CultureInfo.InvariantCulture));
        }
        var title = row.EventType switch
        {
            "SubmissionCreated" => "Booking submitted",
            "StaffBookingCreated" => "Booking created by staff",
            "ReservationConfirmed" => "Booking confirmed",
            "ReservationRejected" => "Booking rejected",
            "ReservationCancelled" => "Booking cancelled",
            "CancellationRequested" => "Cancellation requested",
            "CancellationApproved" => "Cancellation approved",
            "CancellationRejected" or "CancellationDeclined" => "Cancellation declined",
            "ReservationExpired" => "Booking expired",
            "ReservationCheckedIn" => "Customer checked in",
            "ReservationCheckedOut" or "ReservationCompleted" => "Customer checked out",
            "ReservationNoShow" => "Booking marked no-show",
            "ScheduleChangeRequested" => "Schedule change requested",
            "ScheduleChangeApproved" => "Schedule change approved",
            "ScheduleChangeDeclined" => "Schedule change rejected",
            "ScheduleMoved" => "Booking moved",
            "BlockCreated" => "Time blocked",
            "BlockReleased" => "Blocked time released",
            "Merge" => "Customers merged",
            "Undo" => "Customer merge undone",
            "CustomerCreated" => "Customer added",
            "StaffPinReset" => "Team member PIN reset",
            "TeamInvitationResent" => "Activation email resent",
            "TeamMemberCreated" => "Team member added",
            "StaffRolesChanged" => "Team member roles changed",
            "DemoPaymentRecorded" => "Demo payment recorded",
            "CoachConfirmed" => "Coach confirmed",
            _ => row.Category == "Access" ? "Team member access changed" :
                row.Category == "Reservations" && !string.IsNullOrWhiteSpace(row.Status) ? row.Status : "Recorded activity"
        };
        var actor = !string.IsNullOrWhiteSpace(row.ActorLabel) ? row.ActorLabel : row.Actor switch
        {
            "System" => "System",
            "Customer" => "Customer",
            "Demo POS" => "Demo POS",
            "" => "Unknown",
            _ => "Recorded actor"
        };
        var actorId = row.Actor is "System" or "Customer" or "Demo POS" or "" ? null : row.Actor;
        return new(Encode(row.Key), row.OccurredUtc, row.Category, title, actor, actorId,
            row.TargetLabel, row.TargetKind, row.TargetId,
            row.TargetStartUtc is DateTime start ? DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(start, Manila)) : null,
            string.IsNullOrWhiteSpace(row.Reason) ? null : row.Reason, changes);
    }
}
