using HoleInHub.Data.Reservations;

namespace HoleInHub.Business.Reservations;

public sealed record DashboardDay(DateOnly Date, int Booked, int Pending, int Other, IReadOnlyDictionary<string, int> Statuses);
public sealed record DashboardService(string Id, string Label, int Count, double Hours);
public sealed record DashboardHour(int Hour, double Regular, double Vip, double Other);
public sealed record DashboardArrival(string Id, string Reference, string Guest, string Service, DateTime StartUtc, string[] Bays, bool FollowUp);
public sealed record DashboardBay(string Id, string Name, string State);
public sealed record DashboardBaseline(DateOnly From, DateOnly To, int Scheduled, int Booked, int Pending, double BayHours, int Completed);
public sealed record DashboardSnapshot(DateOnly From, DateOnly To, DateTime GeneratedAtUtc, int Scheduled, int ArrivalsDue,
    int CheckedIn, int Completed, IReadOnlyList<DashboardDay> Days, IReadOnlyList<DashboardService> Services,
    IReadOnlyList<DashboardHour> Hours, IReadOnlyList<DashboardArrival> Arrivals,
    IReadOnlyDictionary<string, long> Attention, IReadOnlyList<DashboardBay> Bays)
{
    public DashboardBaseline? Previous { get; init; }
    public int FollowUpsDue { get; init; }
}
public sealed record DashboardBooking(string Id, string Reference, string Guest, string Service, string Status, DateTime StartUtc, DateTime EndUtc, string[] Bays);
public sealed record DashboardSlice(IReadOnlyList<DashboardBooking> Bookings, bool HasMore);

public static class DashboardMetrics
{
    public static readonly string[] BookedStatuses = ["Confirmed", "Checked-In", "Completed"];
    public static DateTime Utc(DateOnly day) => TimeZoneInfo.ConvertTimeToUtc(day.ToDateTime(TimeOnly.MinValue), ReservationRules.Manila);
    public static DateTime Local(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), ReservationRules.Manila);
    public static string ServiceLabel(string id) => id switch
    {
        "Regular" => "Regular", "VIP" => "VIP", "PhilippineCoach" => "Philippine coach",
        "KpgaPro" => "KPGA pro", "KpgaGroup" => "KPGA lessons", "WholeShop" => "Whole shop", _ => string.IsNullOrEmpty(id) ? "Unknown service" : id
    };
    public static void ValidateRange(DateOnly from, DateOnly to)
    {
        if (from < new DateOnly(2000, 1, 1) || to > new DateOnly(2100, 12, 31) || to < from || to.DayNumber - from.DayNumber >= 90)
        {
            throw new ReservationValidationException("Choose a valid range of up to 90 days between 2000 and 2100.");
        }
    }
    public static double Hours(DashboardRow row, DateTime start, DateTime end) =>
        Math.Max(0, (new DateTime(Math.Min(row.EndUtc.Ticks, end.Ticks)) - new DateTime(Math.Max(row.StartUtc.Ticks, start.Ticks))).TotalHours)
        * row.ResourceIds.Distinct().Count();

    public static (DateOnly From, DateOnly To)? PreviousRange(DateOnly from, DateOnly to)
    {
        ValidateRange(from, to);
        var previousFrom = from.AddDays(-(to.DayNumber - from.DayNumber + 1));
        return previousFrom.Year < 2000 ? null : (previousFrom, from.AddDays(-1));
    }

    public static DashboardBaseline Baseline(DateOnly from, DateOnly to, IReadOnlyList<DashboardRow> source)
    {
        ValidateRange(from, to);
        var start = Utc(from); var end = Utc(to.AddDays(1));
        var rows = source.Where(x => x.StartUtc < end && x.EndUtc > start).DistinctBy(x => x.Id).ToArray();
        var booked = rows.Where(x => BookedStatuses.Contains(x.Status)).ToArray();
        return new(from, to, rows.Count(x => BookedStatuses.Contains(x.Status) || x.Status == "Pending"), booked.Length,
            rows.Count(x => x.Status == "Pending" && x.StartUtc >= start), Math.Round(booked.Sum(x => Hours(x, start, end)), 2),
            rows.Count(x => x.Status == "Completed"));
    }

    public static DashboardSnapshot Calculate(DateOnly from, DateOnly to, DateTime now, IReadOnlyList<DashboardRow> source,
        IReadOnlyDictionary<string, long> attention, IReadOnlyDictionary<string, string> resourceTypes, IReadOnlyList<DashboardBay> bays)
    {
        ValidateRange(from, to);
        var start = Utc(from); var end = Utc(to.AddDays(1));
        var rows = source.Where(x => x.StartUtc < end && x.EndUtc > start).DistinctBy(x => x.Id).ToArray();
        var booked = rows.Where(x => BookedStatuses.Contains(x.Status)).ToArray();
        var arrivals = rows.Where(x => x.Status == "Confirmed" && x.CheckedInUtc is null && !x.CancellationRequested && x.EndUtc > now).OrderBy(x => x.StartUtc).ThenBy(x => x.Id).ToArray();
        var days = Enumerable.Range(0, to.DayNumber - from.DayNumber + 1).Select(offset =>
        {
            var day = from.AddDays(offset); var daily = rows.Where(x => DateOnly.FromDateTime(Local(x.StartUtc)) == day).ToArray();
            return new DashboardDay(day, daily.Count(x => BookedStatuses.Contains(x.Status)), daily.Count(x => x.Status == "Pending"),
                daily.Count(x => !BookedStatuses.Contains(x.Status) && x.Status != "Pending"), daily.GroupBy(x => x.Status).ToDictionary(x => x.Key, x => x.Count()));
        }).ToArray();
        var services = booked.GroupBy(x => x.Service).Select(group => new DashboardService(group.Key, ServiceLabel(group.Key), group.Count(),
            Math.Round(group.Sum(row => Hours(row, start, end)), 2))).OrderByDescending(x => x.Count).ThenBy(x => x.Id).ToArray();
        var hours = Enumerable.Range(0, 24).Select(hour =>
        {
            double regular = 0, vip = 0, other = 0;
            foreach (var day in days)
            {
                var bucketStart = Utc(day.Date).AddHours(hour); var bucketEnd = bucketStart.AddHours(1);
                foreach (var row in booked.Where(row => row.StartUtc < bucketEnd && row.EndUtc > bucketStart))
                {
                    var duration = Math.Max(0, (new DateTime(Math.Min(row.EndUtc.Ticks, bucketEnd.Ticks)) - new DateTime(Math.Max(row.StartUtc.Ticks, bucketStart.Ticks))).TotalHours);
                    foreach (var id in row.ResourceIds.Distinct())
                    {
                        switch (resourceTypes.GetValueOrDefault(id)) { case "Regular": regular += duration; break; case "VIP": vip += duration; break; default: other += duration; break; }
                    }
                }
            }
            return new DashboardHour(hour, Math.Round(regular, 2), Math.Round(vip, 2), Math.Round(other, 2));
        }).ToArray();
        return new(from, to, now, rows.Count(x => BookedStatuses.Contains(x.Status) || x.Status == "Pending"), arrivals.Length,
            rows.Count(x => x.Status == "Checked-In"), rows.Count(x => x.Status == "Completed"), days, services, hours,
            arrivals.Take(6).Select(x => new DashboardArrival(x.Id, x.Reference, x.Guest.Trim(), ServiceLabel(x.Service), x.StartUtc, x.ResourceIds,
                now >= AttendanceRules.FollowUpDue(x.StartUtc, x.EndUtc))).ToArray(), attention, bays)
        { FollowUpsDue = arrivals.Count(x => now >= AttendanceRules.FollowUpDue(x.StartUtc, x.EndUtc)) };
    }
}

public sealed partial class ReservationService
{
    public async Task<DashboardSnapshot> GetDashboardAsync(DateOnly from, DateOnly to, bool live, CancellationToken ct)
    {
        DashboardMetrics.ValidateRange(from, to);
        var now = (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime;
        var rows = persistence.ReadDashboardRowsAsync(DashboardMetrics.Utc(from), DashboardMetrics.Utc(to.AddDays(1)), ct);
        var previousRange = DashboardMetrics.PreviousRange(from, to);
        var previousRows = previousRange is { } prior
            ? persistence.ReadDashboardRowsAsync(DashboardMetrics.Utc(prior.From), DashboardMetrics.Utc(prior.To.AddDays(1)), ct) : null;
        var attention = persistence.ReviewCountsAsync(now, ct);
        var catalog = persistence.ReadResourceDirectoryAsync(ct);
        var current = live ? persistence.ReadDashboardLiveAsync(now, ct) : null;
        await Task.WhenAll(rows, attention, catalog);
        if (previousRows is not null)
        {
            await previousRows;
        }
        if (current is not null)
        {
            await current;
        }
        if ((await rows).Count > 20000)
        {
            throw new ReservationValidationException("This range contains too many reservations. Choose a shorter date range.");
        }

        var bays = new List<DashboardBay>();
        if (current is not null)
        {
            var snapshot = await current;
            var local = DashboardMetrics.Local(now);
            foreach (var bay in snapshot.Resources)
            {
                var allocations = snapshot.Bookings.Where(x => x.ResourceIds.Contains(bay.Id)).ToArray();
                var blocked = snapshot.Blocks.Any(x => x.ResourceIds.Length == 0 || x.ResourceIds.Contains(bay.Id));
                var state = allocations.Length > 1 || blocked && allocations.Length > 0 ? "Needs review"
                    : blocked ? "Blocked" : allocations.Length > 0 ? allocations[0].Status == "Pending" ? "Pending hold" : "Booked"
                    : local.Hour < ReservationRules.OpeningHour(local.DayOfWeek) ? "Closed" : "Available";
                bays.Add(new(bay.Id, bay.Name, state));
            }
        }
        var data = DashboardMetrics.Calculate(from, to, now, await rows, await attention, (await catalog).ToDictionary(x => x.Id, x => x.Service), bays);
        return data with { Previous = previousRows is not null && (await previousRows).Count <= 20000 && previousRange is { } range
            ? DashboardMetrics.Baseline(range.From, range.To, await previousRows) : null };
    }

    public async Task<DashboardSlice> GetDashboardSliceAsync(DateOnly from, DateOnly to, string kind, string? value, string? group, int page, CancellationToken ct)
    {
        DashboardMetrics.ValidateRange(from, to);
        if (page is < 1 or > 1000 || value?.Length > 80)
        {
            throw new ReservationValidationException("Choose a valid selection and page.");
        }

        string? service = null; string[]? statuses = null; var exclude = false; var byStart = false;
        (DateTime Start, DateTime End)[] intervals = [];
        if (kind == "day" && DateOnly.TryParseExact(value, "yyyy-MM-dd", out var day) && day >= from && day <= to)
        {
            intervals = [(DashboardMetrics.Utc(day), DashboardMetrics.Utc(day.AddDays(1)))]; byStart = true;
            statuses = group switch { "booked" => DashboardMetrics.BookedStatuses, "pending" => ["Pending"], "other" => [.. DashboardMetrics.BookedStatuses, "Pending"], null or "" => null, _ => throw new ReservationValidationException("Choose a valid status group.") };
            exclude = group == "other";
        }
        else if (kind == "service" && !string.IsNullOrEmpty(value)) { service = value; statuses = DashboardMetrics.BookedStatuses; }
        else if (kind == "hour" && int.TryParse(value, out var hour) && hour is >= 0 and < 24)
        {
            statuses = DashboardMetrics.BookedStatuses;
            intervals = Enumerable.Range(0, to.DayNumber - from.DayNumber + 1).Select(offset =>
                (DashboardMetrics.Utc(from.AddDays(offset)).AddHours(hour), DashboardMetrics.Utc(from.AddDays(offset)).AddHours(hour + 1))).ToArray();
        }
        else if (kind == "all")
        {
            statuses = [.. DashboardMetrics.BookedStatuses, "Pending"];
        }
        else
        {
            throw new ReservationValidationException("Choose a valid chart selection.");
        }

        var rows = await persistence.ReadDashboardSliceAsync(DashboardMetrics.Utc(from), DashboardMetrics.Utc(to.AddDays(1)), service, statuses, exclude, intervals, byStart, page, ct);
        return new(rows.Take(20).Select(x => new DashboardBooking(x.Id, x.Reference, x.Guest.Trim(), DashboardMetrics.ServiceLabel(x.Service), x.Status, x.StartUtc, x.EndUtc, x.ResourceIds)).ToArray(), rows.Count > 20);
    }
}
