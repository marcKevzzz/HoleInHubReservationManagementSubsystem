using HoleInHub.Data.Reservations;

namespace HoleInHub.Business.Reservations;

public static class AttendanceRules
{
    public static DateTime FollowUpDue(DateTime start, DateTime end) => end < start.AddHours(1) ? end : start.AddHours(1);

    public static bool CanCheckIn(ReservationRecord booking, DateTime now) => booking.Status == "Confirmed"
        && booking.StartUtc <= now && now < booking.EndUtc && !booking.CancellationRequested
        && (!BookingRules.IsCoaching(booking.Service) || booking.CoachConfirmed)
        && (booking.Service != "WholeShop" || booking.PaymentStatus == "DepositReceived");

    public static AttendanceChange? Decide(ReservationRecord booking, string action, string reason, DateTime now)
    {
        if (action == "check-in" && CanCheckIn(booking, now))
        {
            return new("Checked-In", "ReservationCheckedIn", now, now, null, true);
        }
        if (action == "check-out" && booking.Status == "Checked-In" && now >= booking.StartUtc
            && (booking.CheckedInUtc is null || now >= booking.CheckedInUtc))
        {
            if (now < booking.EndUtc && string.IsNullOrWhiteSpace(reason))
            {
                throw new ReservationValidationException("Enter a reason for early check-out (up to 500 characters).");
            }
            return new("Completed", "ReservationCompleted", now, null, now, false);
        }
        return null;
    }
}
