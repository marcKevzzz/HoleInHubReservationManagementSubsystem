namespace HoleInHub.Business.Reservations;

public static class ReviewClock
{
    public static DateTime AddMinutes(DateTimeOffset now, int minutes, ReservationPolicy? coverage = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(minutes);
        if (coverage is not null && (coverage.ReviewStart < TimeSpan.Zero || coverage.ReviewEnd > TimeSpan.FromDays(1)
            || coverage.ReviewEnd <= coverage.ReviewStart || !coverage.ReviewDays.Any(day => day is >= 0 and <= 6
                && coverage.ReviewEnd > TimeSpan.FromHours(ReservationRules.OpeningHour((DayOfWeek)day)))))
        {
            throw new ReservationValidationException("Staff review hours are not configured. Please contact the venue.");
        }
        var local = TimeZoneInfo.ConvertTime(now, ReservationRules.Manila).DateTime;
        var remaining = TimeSpan.FromMinutes(minutes);
        while (true)
        {
            var opening = TimeSpan.FromHours(ReservationRules.OpeningHour(local.DayOfWeek));
            var from = coverage is null || coverage.ReviewStart < opening ? opening : coverage.ReviewStart;
            var until = coverage?.ReviewEnd ?? TimeSpan.FromDays(1);
            var enabled = coverage is null || coverage.ReviewDays.Contains((int)local.DayOfWeek);
            if (enabled && from < until && local.TimeOfDay < until)
            {
                if (local.TimeOfDay < from) { local = local.Date + from; }
                var available = local.Date + until - local;
                if (remaining <= available)
                {
                    return TimeZoneInfo.ConvertTimeToUtc(local + remaining, ReservationRules.Manila);
                }
                remaining -= available;
            }
            local = local.Date.AddDays(1);
        }
    }
}
