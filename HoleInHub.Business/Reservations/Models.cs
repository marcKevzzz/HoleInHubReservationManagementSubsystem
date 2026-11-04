namespace HoleInHub.Business.Reservations;

public sealed class SubmitReservationRequest { public string? Email { get; set; } public BayParticipants[] Participants { get; set; } = []; public string Service { get; set; } = "Regular"; public DateOnly Date { get; set; } public TimeOnly StartTime { get; set; } public int DurationMinutes { get; set; } = 60; public string[] ResourceIds { get; set; } = []; public string FirstName { get; set; } = ""; public string LastName { get; set; } = ""; public string Phone { get; set; } = ""; public int Attendees { get; set; } = 1; public string? Notes { get; set; } public bool PrivacyAccepted { get; set; } public string IdempotencyKey { get; set; } = ""; }
public sealed record ResourceDto(string Id, string Name, string Service);
public sealed record AvailabilityDto(ResourceDto Resource, bool Available);
public sealed record ReservationDto(string Id, string Reference, string Service, string Status, DateTime StartUtc, DateTime EndUtc, DateTime ReviewDeadlineUtc, string[] ResourceIds, string FirstName, string LastName, string Phone, int Attendees, IReadOnlyList<ReservationHistoryDto> History, string Notes = "")
{
    public bool IsUpcoming(DateTime nowUtc) => EndUtc > nowUtc && Status is "Pending" or "Confirmed" or "Checked-In";
    public int PolicyVersion { get; init; }
    public Dictionary<string, string> ResourceNames { get; init; } = [];
    public BayParticipants[] Participants { get; init; } = [];
    public string? PaymentReference { get; init; }
    public DateTime CreatedUtc { get; init; }
    public string PaymentStatus { get; init; } = "NotRequired";
    public decimal? RentalTotal { get; init; }
    public decimal? DepositAmount { get; init; }
    public bool CoachConfirmed { get; init; }
    public bool CancellationRequested { get; init; }
}
public sealed record ReservationHistoryDto(string Status, DateTime AtUtc, string Actor, string Reason = "");
public sealed class ReservationValidationException(string message) : Exception(message);
public sealed class ReservationPolicy
{
    public bool Approved { get; set; }
    public bool DurationsApproved { get; set; }
    public TimeSpan ReviewStart { get; set; }
    public TimeSpan ReviewEnd { get; set; }
    public int[] ReviewDays { get; set; } = []; public string[] StaffEmails { get; set; } = [];
    public bool DemoPaymentsEnabled { get; set; }
    public void ValidateCoverage(DateTimeOffset now, int minutes = 30)
    {
        var local = TimeZoneInfo.ConvertTime(now, ReservationRules.Manila); if (!Approved || !DurationsApproved || ReviewStart < TimeSpan.Zero || ReviewEnd > TimeSpan.FromDays(1) || ReviewEnd <= ReviewStart || StaffEmails.Length == 0 || !ReviewDays.Contains((int)local.DayOfWeek) || local.TimeOfDay < ReviewStart || local.TimeOfDay + TimeSpan.FromMinutes(minutes) > ReviewEnd)
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
    public static (DateTime Start, DateTime End) Interval(DateOnly date, TimeOnly time, int duration) { var local = date.ToDateTime(time, DateTimeKind.Unspecified); var opening = date.DayOfWeek == DayOfWeek.Monday ? 11 : 10; if (date == DateOnly.MaxValue || duration < 30 || duration > 840 || duration % 30 != 0 || time.Ticks % TimeSpan.FromMinutes(30).Ticks != 0 || time.Hour < opening || local.AddMinutes(duration) > date.AddDays(1).ToDateTime(TimeOnly.MinValue)) { throw new ReservationValidationException("Choose at least 30 minutes in 30-minute increments within venue opening hours."); } return (TimeZoneInfo.ConvertTimeToUtc(local, Manila), TimeZoneInfo.ConvertTimeToUtc(local.AddMinutes(duration), Manila)); }
}


public sealed class BayParticipants
{
    public string ResourceId { get; set; } = "";
    public int Players { get; set; }
    public int NonPlayers { get; set; }
}

public static class BookingRules
{
    public static bool IsCoaching(string service) => service is "PhilippineCoach" or "KpgaPro" or "KpgaGroup";
    public static int ReviewMinutes(string service) => service == "Regular" ? 30 : 60;
    public static string ResourceService(string service) => service == "VIP" ? "VIP" : "Regular";
    public static void ValidateWindow(string service, DateTime startUtc, DateTimeOffset now)
    {
        ValidateLeadTime(service, startUtc, now);
        var minutes = ReviewMinutes(service);
        var local = TimeZoneInfo.ConvertTime(now, ReservationRules.Manila);
        var opening = local.DayOfWeek == DayOfWeek.Monday ? 11 : 10;
        if (local.Hour < opening || local.TimeOfDay.Add(TimeSpan.FromMinutes(minutes)) > TimeSpan.FromDays(1) || startUtc < now.UtcDateTime.AddMinutes(minutes))
        {
            throw new ReservationValidationException($"Submit during operating hours, at least {minutes} minutes before closing and your session.");
        }
    }
    public static void ValidateLeadTime(string service, DateTime startUtc, DateTimeOffset now)
    {
        if (startUtc < now.UtcDateTime.AddMinutes(ReviewMinutes(service)))
        {
            throw new ReservationValidationException($"Choose a start at least {ReviewMinutes(service)} minutes from now.");
        }
    }
    public static void ValidateGuests(string service, string[] resources, int guests)
    {
        var capacity = resources.Sum(id => id.StartsWith("vip-", StringComparison.Ordinal) ? 10 : 8);
        if (service == "KpgaGroup") { capacity = Math.Min(capacity, 8); }
        if (guests < 1 || guests > capacity)
        {
            throw new ReservationValidationException($"Enter between 1 and {capacity} guests, including non-players, for your selected bays.");
        }
    }
    public static void ValidateParticipants(string service, string[] resources, BayParticipants[] participants)
    {
        if (participants.Length != resources.Length || participants.Select(x => x.ResourceId).Distinct().Count() != resources.Length || participants.Any(x => !resources.Contains(x.ResourceId)))
        {
            throw new ReservationValidationException("Enter players and non-players for each selected bay.");
        }

        foreach (var p in participants)
        {
            var limit = p.ResourceId.StartsWith("vip-", StringComparison.Ordinal) ? 10 : 8;
            if (p.Players is < 0 or > 6 || p.NonPlayers < 0 || p.NonPlayers > limit || p.Players + p.NonPlayers > limit || (service != "WholeShop" && p.Players < 1))
            {
                throw new ReservationValidationException($"Each bay allows six players; total occupancy is {limit} including non-players.");
            }
        }
        if (participants.Sum(x => x.Players) < 1)
        {
            throw new ReservationValidationException("Include at least one player.");
        }

        if (service == "KpgaGroup" && participants.Sum(x => x.Players + x.NonPlayers) > 8)
        {
            throw new ReservationValidationException("The lesson program allows up to eight participants.");
        }
    }
    public static decimal DemoRentalTotal(DateOnly date, int duration)
    {
        var weekend = date.DayOfWeek is DayOfWeek.Friday or DayOfWeek.Saturday or DayOfWeek.Sunday;
        return duration == 360 ? (weekend ? 50000m : 40000m) : (weekend ? 80000m : 60000m);
    }
}

public sealed record RentalQuoteDto(decimal RentalTotal, decimal DepositAmount, string Currency = "PHP", bool Demo = true);

public sealed record ReservationContactDto(string FirstName, string LastName, string Phone);
public sealed record GuestBookingDraftDto(string Email, string ProtectedDraft, bool Verified, DateTime ExpiresUtc, DateTime ResendAfterUtc);

