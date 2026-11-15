using HoleInHub.Data.Reservations;

namespace HoleInHub.Business.Reservations;

public static class SessionExtensionRules
{
    public static bool CanExtend(string status, string service, DateTime start, DateTime end, DateTime now)
    {
        if (status is not ("Confirmed" or "Checked-In") || service is not ("Regular" or "VIP") || start > now || now >= end)
        { return false; }
        var localStart = TimeZoneInfo.ConvertTimeFromUtc(start, ReservationRules.Manila);
        var localEnd = TimeZoneInfo.ConvertTimeFromUtc(end, ReservationRules.Manila);
        return (end - start).TotalMinutes <= 810 && localEnd.AddMinutes(30) <= localStart.Date.AddDays(1);
    }

    public static AttendanceChange? Decide(ReservationRecord booking, int minutes, string reason, DateTime now)
    {
        AttendanceRules.RequireReason(reason);
        if (!CanExtend(booking.Status, booking.Service, booking.StartUtc, booking.EndUtc, now)
            || booking.CancellationRequested || booking.ScheduleChange?.Status == "Pending") { return null; }
        if (minutes is < 30 or > 840 || minutes % 30 != 0)
        { throw new ReservationValidationException("Extend the session in 30-minute increments."); }
        var end = booking.EndUtc.AddMinutes(minutes);
        var localStart = TimeZoneInfo.ConvertTimeFromUtc(booking.StartUtc, ReservationRules.Manila);
        var localEnd = TimeZoneInfo.ConvertTimeFromUtc(end, ReservationRules.Manila);
        if (localEnd > localStart.Date.AddDays(1) || (end - booking.StartUtc).TotalMinutes > 840)
        { throw new ReservationValidationException("The extended session must end by midnight."); }
        return new(booking.Status, "ReservationExtended", now, null, null, true, EndUtc: end);
    }
}
