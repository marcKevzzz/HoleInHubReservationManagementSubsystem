using System.Globalization;
using HoleInHub.Data.Reservations;

namespace HoleInHub.Business.Reservations;

public sealed record ReservationReport(IReadOnlyList<DashboardBooking> Rows, IReadOnlyDictionary<string, string> Services,
    IReadOnlyDictionary<string, int> Volume, IReadOnlyDictionary<string, int> ServiceCounts, IReadOnlyDictionary<string, double> BayHours,
    int NewCustomers, int ReturningCustomers, int UnmatchedReservations)
{
    public BookingOutcomes Outcomes { get; init; } = new(default, default, default, default, default);
    public IReadOnlyList<OutcomeWeek> Weeks { get; init; } = [];
}

public sealed partial class ReservationService
{
    public async Task<(Dictionary<string, int> Counts, DashboardBooking[] Rows, int Page, int Pages)> GetReportListAsync(
        DateOnly from, DateOnly to, string? service, string? status, int page, CancellationToken ct)
    {
        DashboardMetrics.ValidateRange(from, to);
        var start = DashboardMetrics.Utc(from);
        var end = DashboardMetrics.Utc(to.AddDays(1));
        var (counts, total) = await persistence.ReadReportStatusCountsAsync(start, end, service, ct);
        ValidateReportSize(total);
        var matches = string.IsNullOrEmpty(status) ? counts.Values.Sum() : counts.GetValueOrDefault(status);
        var pages = Math.Max(1, (matches + 19) / 20);
        page = Math.Clamp(page, 1, pages);
        var rows = await persistence.ReadDashboardSliceAsync(start, end, string.IsNullOrEmpty(service) ? null : service,
            string.IsNullOrEmpty(status) ? null : [status], false, [], false, page, ct);
        return (counts, rows.Take(20).Select(ToScheduleBooking).ToArray(), page, pages);
    }

    private static void ValidateReportSize(int count)
    {
        if (count > 20000)
        {
            throw new ReservationValidationException("This range contains too many reservations. Choose a shorter date range.");
        }
    }

    public async Task<ReservationReport> GetReportAsync(DateOnly from, DateOnly to, string? service, CancellationToken ct)
    {
        DashboardMetrics.ValidateRange(from, to);
        var start = DashboardMetrics.Utc(from);
        var source = await persistence.ReadDashboardRowsAsync(start, DashboardMetrics.Utc(to.AddDays(1)), ct);
        ValidateReportSize(source.Count);

        var services = source.Select(row => row.Service).Distinct().Order(StringComparer.Ordinal)
            .ToDictionary(id => id, DashboardMetrics.ServiceLabel);
        var rows = source.Where(row => string.IsNullOrEmpty(service) || row.Service == service)
            .OrderBy(row => row.StartUtc).ThenBy(row => row.Id, StringComparer.Ordinal).ToArray();
        var (profiles, returning) = await persistence.ReadReportProfilesAsync(rows.Select(row => row.Id).ToArray(), start, ct);
        var outcomes = await persistence.ReadOutcomeRowsAsync(start, DashboardMetrics.Utc(to.AddDays(1)), service, ct);
        ValidateReportSize(outcomes.Count);
        var weeks = new List<OutcomeWeek>();
        for (var day = from; day <= to;)
        {
            var last = day.AddDays(Math.Min(to.DayNumber - day.DayNumber, (7 - (int)day.DayOfWeek) % 7));
            weeks.Add(new(day, last, BookingOutcomes.ForPeriod(outcomes, day, last)));
            day = last.AddDays(1);
        }
        return CalculateReport(from, to, rows, services, profiles, returning) with
        {
            Outcomes = BookingOutcomes.ForPeriod(outcomes, from, to),
            Weeks = weeks
        };
    }

    public static ReservationReport CalculateReport(DateOnly from, DateOnly to, IReadOnlyList<DashboardRow> rows,
        IReadOnlyDictionary<string, string> services, IReadOnlyDictionary<string, string> profiles, IReadOnlySet<string> returning)
    {
        var start = DashboardMetrics.Utc(from);
        var end = DashboardMetrics.Utc(to.AddDays(1));
        static string Hour(int hour) => hour.ToString("00", CultureInfo.InvariantCulture) + ":00";
        static string Day(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var hours = Enumerable.Range(0, 24).ToDictionary(Hour, _ => 0d);
        var volume = from == to ? hours.Keys.ToDictionary(hour => hour, _ => 0)
            : Enumerable.Range(0, to.DayNumber - from.DayNumber + 1).ToDictionary(offset => Day(from.AddDays(offset)), _ => 0);
        var countedClasses = new HashSet<string>();
        foreach (var row in rows)
        {
            var firstIncluded = row.PackageDates?.Where(d => d.StartUtc < end && d.EndUtc > start)
                .Select(d => d.StartUtc).DefaultIfEmpty(row.StartUtc).Min() ?? row.StartUtc;
            var clippedStart = firstIncluded < start ? start : firstIncluded;
            var local = DashboardMetrics.Local(clippedStart);
            volume[from == to ? Hour(local.Hour) : Day(DateOnly.FromDateTime(local))]++;
            if (!DashboardMetrics.BookedStatuses.Contains(row.Status)) { continue; }
            if (row.PackageDates != null)
            {
                foreach (var date in row.PackageDates.Where(x => x.StartUtc < end && x.EndUtc > start && countedClasses.Add(x.ClassSessionId)))
                { AddHours(date.StartUtc, date.EndUtc, date.ResourceIds); }
            }
            else if (row.ClassSessionId == null || countedClasses.Add(row.ClassSessionId))
            { AddHours(row.StartUtc, row.EndUtc, row.ResourceIds); }
        }
        void AddHours(DateTime begins, DateTime ends, string[] resources)
        {
            var clippedStart = begins < start ? start : begins;
            var clippedEnd = ends > end ? end : ends;
            var bays = resources.Distinct().Count();
            for (var cursor = clippedStart; cursor < clippedEnd;)
            {
                var nextHour = cursor.Date.AddHours(cursor.Hour + 1);
                var next = nextHour < clippedEnd ? nextHour : clippedEnd;
                hours[Hour(DashboardMetrics.Local(cursor).Hour)] += (next - cursor).TotalHours * bays;
                cursor = next;
            }
        }
        var bookings = rows.Select(ToScheduleBooking).ToArray();
        var customerIds = profiles.Values.Distinct().ToArray();
        var repeat = customerIds.Count(returning.Contains);
        return new(bookings, services, volume, bookings.GroupBy(row => row.Service).ToDictionary(group => group.Key, group => group.Count()),
            hours, customerIds.Length - repeat, repeat, rows.Count - profiles.Count);
    }

    private static DashboardBooking ToScheduleBooking(DashboardRow row) => ToDashboardBooking(row) with
    {
        Bays = row.ResourceIds.Select(id => row.ResourceNames.GetValueOrDefault(id, id)).ToArray()
    };
}
