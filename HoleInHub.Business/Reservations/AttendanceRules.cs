using HoleInHub.Data.Reservations;

namespace HoleInHub.Business.Reservations;

public static class AttendanceRules
{
    public static DateTime FollowUpDue(DateTime start, DateTime end) => end < start.AddHours(1) ? end : start.AddHours(1);
    public static DateTime CheckInOpens(DateTime start) => start.AddHours(-1);

    public static bool CanCheckIn(ReservationRecord booking, DateTime now) => booking.Status == "Confirmed"
        && CheckInOpens(booking.StartUtc) <= now && now < booking.EndUtc && !booking.CancellationRequested
        && (!BookingRules.IsCoaching(booking.Service) || booking.CoachConfirmed)
        && (booking.Service != "WholeShop" || booking.PaymentStatus == "DepositReceived");

    public static bool CanCheckOut(ReservationRecord booking, DateTime now) => booking.Status == "Checked-In"
        && now >= (booking.CheckedInUtc ?? booking.StartUtc);

    public static bool CanMarkNoShow(ReservationRecord booking, DateTime now) => booking.Status == "Confirmed"
        && booking.CheckedInUtc is null && booking.CheckedOutUtc is null && !booking.CancellationRequested
        && booking.ScheduleChange?.Status != "Pending"
        && now >= FollowUpDue(booking.StartUtc, booking.EndUtc);

    public static AttendanceChange? Decide(ReservationRecord booking, string action, string reason, DateTime now)
    {
        if (action == "no-show" && CanMarkNoShow(booking, now))
        {
            RequireReason(reason);
            return new("No-show", "ReservationNoShow", now, null, null, false);
        }
        if (action == "check-in" && CanCheckIn(booking, now))
        {
            return new("Checked-In", "ReservationCheckedIn", now, now, null, true);
        }
        if (action == "check-out" && CanCheckOut(booking, now))
        {
            if (now < booking.EndUtc && string.IsNullOrWhiteSpace(reason))
            {
                throw new ReservationValidationException("Enter a reason for early check-out (up to 500 characters).");
            }
            return new("Completed", "ReservationCompleted", now, null, now, false);
        }
        return null;
    }

    public static void RequireReason(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > 500)
        { throw new ReservationValidationException("Enter a reason of up to 500 characters."); }
    }
}
