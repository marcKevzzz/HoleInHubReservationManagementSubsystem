namespace HoleInHub.Business.Reservations;

public sealed record WholeShopSessionAvailability(int StartMinute, int Duration, bool Available);
public sealed record WholeShopDayAvailability(DateOnly Date, IReadOnlyList<WholeShopSessionAvailability> Sessions);

public sealed partial class ReservationService
{
    public async Task<IReadOnlyList<WholeShopDayAvailability>> GetWholeShopCalendarAsync(int year, int month,
        string? serviceEntryId, CancellationToken ct)
    {
        if (year is < 1970 or > 9998 || month is < 1 or > 12)
        { throw new ReservationValidationException("Choose a valid calendar month."); }
        var service = await ResolveBookingServiceAsync("WholeShop", serviceEntryId, ct);
        var first = new DateOnly(year, month, 1);
        var next = first.AddMonths(1);
        var start = TimeZoneInfo.ConvertTimeToUtc(first.ToDateTime(TimeOnly.MinValue), ReservationRules.Manila);
        var end = TimeZoneInfo.ConvertTimeToUtc(next.ToDateTime(TimeOnly.MinValue), ReservationRules.Manila);
        var now = (clock ?? TimeProvider.System).GetUtcNow();
        await ProcessPendingAsync(ct);
        var bays = await GetCatalogAsync(ct);
        var occupied = await persistence.ReadOccupiedIntervalsAsync(start, end, bays.Select(bay => bay.Id).ToArray(), now.UtcDateTime, ct);
        var days = new List<WholeShopDayAvailability>();
        for (var day = first; day < next; day = day.AddDays(1))
        {
            var opening = ReservationRules.OpeningHour(day.DayOfWeek) * 60;
            var sessions = new List<WholeShopSessionAvailability>();
            foreach (var (minute, duration) in new[] { (900, 360), (opening, 1440 - opening) })
            {
                var available = false;
                try
                {
                    var interval = PreviewServiceInterval(service, day, new TimeOnly(minute / 60, minute % 60), duration, "WholeShop");
                    BookingRules.ValidateLeadTime("WholeShop", interval.Start, now);
                    available = bays.Count > 0 && !occupied.Any(item => item.StartUtc < interval.End && item.EndUtc > interval.Start);
                }
                catch (ReservationValidationException) { }
                sessions.Add(new(minute, duration, available));
            }
            days.Add(new(day, sessions));
        }
        return days;
    }
}
