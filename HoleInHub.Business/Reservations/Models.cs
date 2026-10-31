namespace HoleInHub.Business.Reservations;

public sealed class SubmitReservationRequest { public string Service { get; set; } = "Regular"; public DateOnly Date { get; set; } public TimeOnly StartTime { get; set; } public int DurationMinutes { get; set; } = 60; public string[] ResourceIds { get; set; } = []; public string FirstName { get; set; } = ""; public string LastName { get; set; } = ""; public string Phone { get; set; } = ""; public int Attendees { get; set; } = 1; public string IdempotencyKey { get; set; } = ""; }
public sealed record ResourceDto(string Id, string Name, string Service);
public sealed record AvailabilityDto(ResourceDto Resource, bool Available);
public sealed record ReservationDto(string Id, string Reference, string Service, string Status, DateTime StartUtc, DateTime EndUtc, DateTime ReviewDeadlineUtc, string[] ResourceIds, string FirstName, string LastName, string Phone, int Attendees, IReadOnlyList<ReservationHistoryDto> History);
public sealed record ReservationHistoryDto(string Status, DateTime AtUtc, string Actor);
public sealed class ReservationValidationException(string message) : Exception(message);
public sealed class ReservationPolicy
{
    public bool Approved { get; set; }
    public bool DurationsApproved { get; set; }
    public TimeSpan ReviewStart { get; set; }
    public TimeSpan ReviewEnd { get; set; }
    public int[] ReviewDays { get; set; } = []; public string[] StaffEmails { get; set; } = [];
    public void ValidateCoverage(DateTimeOffset now)
    {
        var local = TimeZoneInfo.ConvertTime(now, ReservationRules.Manila); if (!Approved || !DurationsApproved || ReviewStart < TimeSpan.Zero || ReviewEnd > TimeSpan.FromDays(1) || ReviewEnd <= ReviewStart || StaffEmails.Length == 0 || !ReviewDays.Contains((int)local.DayOfWeek) || local.TimeOfDay < ReviewStart || local.TimeOfDay + TimeSpan.FromMinutes(30) > ReviewEnd)
        {
            throw new ReservationValidationException("Online requests are unavailable until an approved staff review window and service policy apply. Please contact the venue.");
        }
    }
}
public static class ReservationRules
{
    public static TimeZoneInfo Manila { get; } = TimeZoneInfo.FindSystemTimeZoneById("Asia/Manila");
    public static (DateTime Start, DateTime End) PreviewInterval(DateOnly date, TimeOnly time, int duration, string service)
    {
        if (service is not ("Regular" or "VIP" or "PhilippineCoach" or "KpgaPro" or "KpgaGroup" or "WholeShop"))
        {
            throw new ReservationValidationException("Choose a listed experience.");
        }
        if (service == "KpgaPro" && date.DayOfWeek is not (DayOfWeek.Tuesday or DayOfWeek.Thursday or DayOfWeek.Saturday))
        {
            throw new ReservationValidationException("KPGA pro coaching is available Tuesday, Thursday and Saturday.");
        }
        if (service == "KpgaGroup" && (date.DayOfWeek != DayOfWeek.Tuesday || time != new TimeOnly(15, 0) || duration != 60))
        {
            throw new ReservationValidationException("KPGA lessons run Tuesday, 3–4 PM.");
        }
        if (service == "WholeShop")
        {
            var opening = new TimeOnly(date.DayOfWeek == DayOfWeek.Monday ? 11 : 10, 0);
            if (date == DateOnly.MaxValue || !((time == new TimeOnly(15, 0) && duration == 360) || (time == opening && duration == 1440 - opening.Hour * 60)))
            {
                throw new ReservationValidationException("Choose half day (3–9 PM) or whole day (opening–midnight).");
            }
            var local = date.ToDateTime(time, DateTimeKind.Unspecified);
            return (TimeZoneInfo.ConvertTimeToUtc(local, Manila), TimeZoneInfo.ConvertTimeToUtc(local.AddMinutes(duration), Manila));
        }
        return Interval(date, time, duration);
    }
    public static (DateTime Start, DateTime End) Interval(DateOnly date, TimeOnly time, int duration) { var local = date.ToDateTime(time, DateTimeKind.Unspecified); var opening = date.DayOfWeek == DayOfWeek.Monday ? 11 : 10; if (date == DateOnly.MaxValue || duration < 30 || duration > 600 || duration % 30 != 0 || time.Ticks % TimeSpan.FromMinutes(30).Ticks != 0 || time.Hour < opening || local.AddMinutes(duration) > date.AddDays(1).ToDateTime(TimeOnly.MinValue)) { throw new ReservationValidationException("Choose a 30-minute increment within venue opening hours (maximum 10 hours)."); } return (TimeZoneInfo.ConvertTimeToUtc(local, Manila), TimeZoneInfo.ConvertTimeToUtc(local.AddMinutes(duration), Manila)); }
}

