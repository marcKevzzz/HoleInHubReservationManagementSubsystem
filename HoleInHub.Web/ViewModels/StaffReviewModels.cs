using HoleInHub.Business.Reservations;

namespace HoleInHubReservationManagementSubsystem.ViewModels;

public sealed record StaffReviewQueue(IReadOnlyList<ReservationDto> Items, string Filter, string? Day, string Sort, DateTime Now)
{
    public IEnumerable<ReservationDto> Visible => (Sort == "session"
        ? Items.OrderBy(x => !Overdue(x)).ThenBy(x => x.StartUtc)
        : Filter == "expired" ? Items.OrderByDescending(x => x.ReviewDeadlineUtc).ThenBy(x => x.StartUtc)
        : Items.OrderBy(x => Deadline(x) ?? DateTime.MaxValue).ThenBy(x => x.StartUtc))
        .Where(x => (Filter == "expired" ? x.Status == "Expired" : Filter == "all" || Type(x) == Filter)
            && (Day is null || Local(x.StartUtc).ToString("yyyy-MM-dd") == Day));
    public bool Overdue(ReservationDto booking) => Deadline(booking) is { } deadline && deadline <= Now;
    public static string Type(ReservationDto x) => x.CancellationRequested ? "cancellations"
        : x.ScheduleChange?.Status == "Pending" ? "changes" : x.PaymentStatus == "LatePayment" || x.Service == "WholeShop" ? "deposit" : "pending";
    public static string Label(ReservationDto x) => Type(x) switch
    {
        "cancellations" => "Cancellation request",
        "changes" => "Schedule change",
        "deposit" => x.PaymentStatus is "LatePayment" or "Failed" or "CheckoutCancelled" ? "Deposit issue" : "Awaiting deposit",
        _ => "Pending review"
    };
    public static DateTime? Deadline(ReservationDto x) => x.Status == "Pending" ? x.ReviewDeadlineUtc : null;
    public static DateTime Local(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(utc, ReservationRules.Manila);
    public static string Bays(ReservationDto x) => x.Service == "WholeShop" ? "All bays"
        : string.Join(", ", x.ResourceIds.Select(id => x.ResourceNames.GetValueOrDefault(id, id)));
}

public sealed record StaffReviewDetail(StaffReviewContext Context, StaffReviewQueue Queue, IReadOnlyList<StaffScheduleBay> Bays,
    bool FromQueue, string? ScheduleDate, string? Previous, string? Next, StaffAttendanceForm? AttendanceForm = null);

public sealed record StaffDeadline(DateTime Due, DateTime Now, bool Deposit)
{
    public string Urgency => Due <= Now ? "overdue" : Due <= Now.AddMinutes(5) ? "critical" : Due <= Now.AddMinutes(15) ? "soon" : "normal";
    public string Countdown
    {
        get
        {
            var minutes = (int)Math.Ceiling((Due - Now).TotalMinutes);
            return minutes <= 0 ? "Overdue" : minutes < 60 ? $"{minutes}m left" : $"{minutes / 60}h {minutes % 60}m left";
        }
    }
}
