using HoleInHub.Business.Reservations;

namespace HoleInHubReservationManagementSubsystem.ViewModels;

public sealed class StaffTimeline(DateOnly day, IReadOnlyList<StaffScheduleBay> bays)
{
    private readonly DateTime midnight = day.ToDateTime(TimeOnly.MinValue);
    public int FirstHour { get; } = bays.SelectMany(bay => bay.Entries)
        .Select(entry => DateOnly.FromDateTime(entry.Start) < day ? 0 : entry.Start.Hour)
        .Append(ReservationRules.OpeningHour(day.DayOfWeek)).Min();
    public int Hours => 24 - FirstHour;

    public IReadOnlyList<StaffScheduleEntry> Bookings { get; } = bays.SelectMany(bay => bay.Entries)
        .Where(entry => entry.ReservationId is not null)
        .DistinctBy(entry => entry.ReservationId).OrderBy(entry => entry.Start).ToArray();

    public IReadOnlyList<(StaffScheduleBay Bay, IReadOnlyList<StaffTimelineBar> Coverage)> DisplayBays()
    {
        var rows = new List<(StaffScheduleBay, IReadOnlyList<StaffTimelineBar>)>();
        var buyouts = Bookings.Where(entry => entry.Service == "WholeShop").ToArray();
        if (buyouts.Length > 0)
        {
            rows.Add((new("Venue buyout", true, buyouts, "WholeShop"), []));
        }
        rows.AddRange(bays.OrderBy(bay => bay.Service == "VIP")
            .Select(bay => (
                bay with { Entries = bay.Entries.Where(entry => entry.Service != "WholeShop").ToArray() },
                Bars(bay with { Entries = bay.Entries.Where(entry => entry.Service == "WholeShop" && entry.HoldsCapacity).ToArray() }))));
        return rows;
    }

    public (int Reserved, int Capacity, DateTime Time) Peak()
    {
        var active = bays.Where(bay => bay.Active).ToArray();
        var end = midnight.AddDays(1);
        var starts = active.SelectMany(bay => bay.Entries)
            .Where(entry => entry.ReservationId is not null && entry.HoldsCapacity && entry.Start < end && entry.End > midnight)
            .Select(entry => entry.Start < midnight ? midnight : entry.Start)
            .Distinct().Order();
        var peak = 0;
        var peakTime = midnight;
        foreach (var time in starts)
        {
            var reserved = active.Count(bay => bay.Entries.Any(entry =>
                entry.ReservationId is not null && entry.HoldsCapacity && entry.Start <= time && entry.End > time));
            if (reserved > peak)
            {
                peak = reserved;
                peakTime = time;
            }
        }
        return (peak, active.Length, peakTime);
    }

    public IReadOnlyList<StaffTimelineBar> Bars(StaffScheduleBay bay)
    {
        var start = midnight.AddHours(FirstHour);
        var end = midnight.AddDays(1);
        var laneEnds = new List<DateTime>();
        var bars = new List<StaffTimelineBar>();

        foreach (var entry in bay.Entries.OrderBy(entry => entry.Start).ThenBy(entry => entry.End))
        {
            var left = entry.Start < start ? start : entry.Start;
            var right = entry.End > end ? end : entry.End;
            if (right <= left)
            {
                continue;
            }

            var lane = laneEnds.FindIndex(until => until <= left);
            if (lane < 0)
            {
                lane = laneEnds.Count;
                laneEnds.Add(right);
            }
            else
            {
                laneEnds[lane] = right;
            }
            bars.Add(new(entry, lane, (left - start).TotalHours / Hours * 100, (right - left).TotalHours / Hours * 100));
        }
        return bars;
    }
}

public sealed record StaffTimelineBar(StaffScheduleEntry Entry, int Lane, double StartPercent, double WidthPercent);
