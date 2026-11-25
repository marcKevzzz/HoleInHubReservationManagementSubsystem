using System.Globalization;
using HoleInHub.Data.Reservations;

namespace HoleInHub.Business.Reservations;

public sealed record CoachDto(string Id, string Name, bool Active, long Revision, CoachWeeklyHoursDto[] WeeklyHours, CoachDateHoursDto[] Overrides, string? Phone = null, string? Email = null, string? Title = null);
public sealed record CoachWeeklyHoursDto(DayOfWeek Day, int StartMinute, int EndMinute);
public sealed record CoachDateHoursDto(string Date, int? StartMinute, int? EndMinute);

public sealed record CoachBookingConflictDto(string Id, string Reference, DateTime StartUtc, DateTime EndUtc, string Status);

public sealed class CoachCommitmentException(CoachBookingConflictDto[] bookings) : Exception("Review the affected bookings before changing this coach's schedule.")
{
    public CoachBookingConflictDto[] Bookings { get; } = bookings;
    public string[] ReservationIds => Bookings.Select(x => x.Id).ToArray();
}

public sealed partial class ReservationService
{
    public const string CoachUnavailable = "No coach and bay are available for the full session. Choose another time.";
    public bool PhilippineCoachSchedulesEnabled => policy.Value.PhilippineCoachSchedulesEnabled;
    private bool RequiresCoach(string service) => PhilippineCoachSchedulesEnabled && service == "PhilippineCoach";

    public async Task<IReadOnlyList<CoachDto>> CoachesAsync(CancellationToken ct) =>
        (await persistence.ReadCoachesAsync(ct)).Select(MapCoach).ToArray();

    public async Task<IReadOnlyList<CoachDto>> AvailableCoachesAsync(string reservationId, CancellationToken ct)
    {
        var booking = await persistence.ByIdAsync(reservationId, ct);
        var now = (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime;
        if (booking is null || booking.Service != "PhilippineCoach" || booking.Status is not ("Pending" or "Confirmed")
            || booking.StartUtc <= now) { return []; }
        return (await persistence.AvailableCoachesAsync(booking.StartUtc, booking.EndUtc, now, ct, reservationId)).Select(MapCoach).ToArray();
    }

    public async Task<bool> AssignCoachAsync(string reservationId, long revision, string coachId, string actor, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(coachId) || revision < 0 || string.IsNullOrWhiteSpace(actor))
        { throw new ReservationValidationException("Choose an available coach and reload the booking if it has changed."); }
        try { return await persistence.AssignCoachAsync(reservationId, revision, coachId, actor, (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime, ct); }
        catch (AllocationConflictException ex) { throw new ReservationValidationException(ex.Message); }
    }

    public async Task<bool> SaveCoachAsync(CoachDto coach, long? expectedRevision, CancellationToken ct)
    {
        ValidateCoach(coach);
        if (expectedRevision is < 0 || (expectedRevision.HasValue && string.IsNullOrWhiteSpace(coach.Id)))
        { throw new ReservationValidationException("Reload the coach profile before saving."); }
        var record = new CoachRecord
        {
            Id = expectedRevision.HasValue ? coach.Id : Guid.NewGuid().ToString("N"),
            Name = coach.Name.Trim(),
            Title = coach.Title?.Trim(),
            Phone = coach.Phone?.Trim(),
            Email = coach.Email?.Trim(),
            Active = coach.Active,
            WeeklyHours = coach.WeeklyHours.Select(w => new CoachWeeklyHours(w.Day, w.StartMinute, w.EndMinute)).ToArray(),
            Overrides = coach.Overrides.Select(w => new CoachDateHours(w.Date, w.StartMinute, w.EndMinute)).ToArray()
        };
        try { return await persistence.SaveCoachAsync(record, expectedRevision, (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime, ct); }
        catch (CoachScheduleConflictException ex)
        {
            throw new CoachCommitmentException(ex.Bookings.Select(x =>
                new CoachBookingConflictDto(x.Id, x.Reference, x.StartUtc, x.EndUtc, x.Status)).ToArray());
        }
    }

    public static void ValidateCoach(CoachDto coach)
    {
        if (string.IsNullOrWhiteSpace(coach.Name) || coach.Name.Trim().Length > 100 || coach.Name.Any(char.IsControl))
        { throw new ReservationValidationException("Enter a coach name of up to 100 characters."); }
        if (coach.WeeklyHours is null || coach.Overrides is null)
        { throw new ReservationValidationException("Check the coach's weekly hours and date overrides."); }
        foreach (var day in coach.WeeklyHours.GroupBy(row => row.Day))
        {
            if (!Enum.IsDefined(day.Key)) { throw new ReservationValidationException("Choose a valid weekday."); }
            ValidateWindows(day.Select(row => (row.StartMinute, row.EndMinute)));
        }
        foreach (var date in coach.Overrides.GroupBy(row => row.Date))
        {
            if (!DateOnly.TryParseExact(date.Key, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            { throw new ReservationValidationException("Enter a valid date for each override."); }
            if (date.Any(row => row.StartMinute is null || row.EndMinute is null))
            {
                if (date.Count() != 1 || date.First().StartMinute.HasValue || date.First().EndMinute.HasValue)
                { throw new ReservationValidationException("For a closed date leave both times blank and remove other windows on that date."); }
                continue;
            }
            ValidateWindows(date.Select(row => (row.StartMinute!.Value, row.EndMinute!.Value)));
        }
    }

    private static void ValidateWindows(IEnumerable<(int Start, int End)> windows)
    {
        var previousEnd = 0;
        foreach (var (start, end) in windows.OrderBy(window => window.Start))
        {
            if (start < previousEnd || end <= start || end > 1440)
            { throw new ReservationValidationException("Enter non-overlapping windows with an end time later than the start on the same day."); }
            previousEnd = end;
        }
    }

    private static CoachDto MapCoach(CoachRecord coach) => new(coach.Id, coach.Name, coach.Active, coach.Revision,
        coach.WeeklyHours.Select(w => new CoachWeeklyHoursDto(w.Day, w.StartMinute, w.EndMinute)).ToArray(),
        coach.Overrides.Select(w => new CoachDateHoursDto(w.Date, w.StartMinute, w.EndMinute)).ToArray(), coach.Phone, coach.Email, coach.Title);
}
