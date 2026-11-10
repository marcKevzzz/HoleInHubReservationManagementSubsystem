using HoleInHub.Business.Reservations;

namespace HoleInHubReservationManagementSubsystem.ViewModels;

public sealed record StaffReviewQueue(IReadOnlyList<ReservationDto> Items, string Filter, string? Day, string Sort, DateTime Now)
{
    public const string DefaultSection = "pending";
    public static IReadOnlyList<(string Key, string Label, string Title)> Sections { get; } =
    [
        ("pending", "Pending", "Pending reviews"),
        ("overdue", "Overdue", "Overdue requests"),
        ("changes", "Schedule changes", "Schedule changes"),
        ("cancellations", "Cancellations", "Cancellation requests"),
        ("deposit", "Deposits", "Deposits"),
        ("expired", "Expired requests", "Expired requests")
    ];

    public static string SectionKey(string? key) => key == "closed" ? "expired"
        : Sections.Any(section => section.Key == key) ? key! : DefaultSection;

    public IEnumerable<ReservationDto> Visible
    {
        get
        {
            var matches = Items.Where(x => (Day is null || Local(x.StartUtc).ToString("yyyy-MM-dd") == Day) && Matches(x));
            return Sort == "session"
                ? matches.OrderBy(x => !Overdue(x)).ThenBy(x => x.StartUtc)
                : Filter == "expired"
                    ? matches.OrderByDescending(x => x.ReviewDeadlineUtc).ThenBy(x => x.StartUtc)
                    : matches.OrderBy(x => Deadline(x) ?? DateTime.MaxValue).ThenBy(x => x.StartUtc);
        }
    }

    private bool Matches(ReservationDto booking) => Filter switch
    {
        "expired" => booking.Status == "Expired",
        "overdue" => ReservationService.IsReviewOverdue(booking, Now),
        _ => Type(booking) == Filter && !(Filter == "pending" && ReservationService.IsReviewOverdue(booking, Now))
    };
    public bool Overdue(ReservationDto booking) => Deadline(booking) is { } deadline && deadline <= Now;
    public static string Type(ReservationDto x) => x.CancellationRequested ? "cancellations"
        : x.ScheduleChange?.Status == "Pending" ? "changes" : x.PaymentStatus == "LatePayment" || x.Service == "WholeShop" ? "deposit" : "pending";
    public static string Label(ReservationDto x, DateTime now) => Type(x) switch
    {
        "cancellations" => "Cancellation request",
        "changes" => "Schedule change",
        "deposit" => x.PaymentStatus is "LatePayment" or "Failed" or "CheckoutCancelled" ? "Deposit issue" : "Awaiting deposit",
        _ => ReservationService.IsReviewOverdue(x, now) ? "Pending · Overdue" : "Pending review"
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
