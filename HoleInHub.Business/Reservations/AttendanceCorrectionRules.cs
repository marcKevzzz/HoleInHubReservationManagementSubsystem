using HoleInHub.Data.Reservations;

namespace HoleInHub.Business.Reservations;

public static class AttendanceCorrectionRules
{
    public static bool CanCorrect(string status) => status is "Confirmed" or "Checked-In" or "Completed" or "No-show";

    public static AttendanceChange? Decide(ReservationRecord booking, string? status, DateTime? arrival, DateTime? departure,
        string reason, DateTime now)
    {
        AttendanceRules.RequireReason(reason);
        if (!CanCorrect(booking.Status) || booking.CancellationRequested || booking.ScheduleChange?.Status == "Pending") { return null; }
        if (status is not ("Confirmed" or "Checked-In" or "Completed" or "No-show"))
        { throw new ReservationValidationException("Choose a valid attendance status."); }
        if (arrival?.Kind is DateTimeKind.Local or DateTimeKind.Unspecified || departure?.Kind is DateTimeKind.Local or DateTimeKind.Unspecified
            || arrival > now || departure > now || arrival < AttendanceRules.CheckInOpens(booking.StartUtc) || arrival >= booking.EndUtc)
        { throw new ReservationValidationException("Use actual attendance times within the session's check-in window and no later than now."); }
        if ((status is "Confirmed" or "No-show") && (arrival.HasValue || departure.HasValue)
            || status == "Checked-In" && (!arrival.HasValue || departure.HasValue)
            || status == "Completed" && (!arrival.HasValue || !departure.HasValue || departure < arrival))
        { throw new ReservationValidationException("Checked in needs arrival only. Completed needs arrival and departure in order. Clear both times for Confirmed or No-show."); }
        if (status == "No-show" && now < AttendanceRules.FollowUpDue(booking.StartUtc, booking.EndUtc))
        { throw new ReservationValidationException("The arrival follow-up deadline has not been reached."); }
        if (booking.Status == status && booking.CheckedInUtc == arrival && booking.CheckedOutUtc == departure)
        { throw new ReservationValidationException("Change the status or actual attendance times before saving a correction."); }
        if (status is "Confirmed" or "Checked-In" &&
            (BookingRules.IsCoaching(booking.Service) && !booking.CoachConfirmed
             || booking.Service == "WholeShop" && booking.PaymentStatus != "DepositReceived"))
        { throw new ReservationValidationException("Restore coach confirmation or verified deposit status before restoring this reservation."); }
        return new(status, "AttendanceCorrected", now, arrival, departure, status is "Confirmed" or "Checked-In", ReplaceActualTimes: true);
    }
}
