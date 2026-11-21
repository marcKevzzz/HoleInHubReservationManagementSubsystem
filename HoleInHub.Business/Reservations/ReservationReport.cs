using System.Globalization;
using HoleInHub.Data.Reservations;

namespace HoleInHub.Business.Reservations;

public sealed record ReservationReport(IReadOnlyList<DashboardBooking> Rows, IReadOnlyDictionary<string, string> Services,
    IReadOnlyDictionary<string, int> Volume, IReadOnlyDictionary<string, int> ServiceCounts, IReadOnlyDictionary<string, double> BayHours,
    int NewCustomers, int ReturningCustomers, int UnmatchedReservations);

public sealed partial class ReservationService
{
    public async Task<ReservationReport> GetReportAsync(DateOnly from, DateOnly to, string? service, CancellationToken ct)
    {
        DashboardMetrics.ValidateRange(from, to);
        await ProcessPendingAsync(ct);
        var start = DashboardMetrics.Utc(from);
        var source = await persistence.ReadDashboardRowsAsync(start, DashboardMetrics.Utc(to.AddDays(1)), ct);
        if (source.Count > 20000)
        {
            throw new ReservationValidationException("This range contains too many reservations. Choose a shorter date range.");
        }

        var services = source.Select(row => row.Service).Distinct().Order(StringComparer.Ordinal)
            .ToDictionary(id => id, DashboardMetrics.ServiceLabel);
        var rows = source.Where(row => string.IsNullOrEmpty(service) || row.Service == service)
            .OrderBy(row => row.StartUtc).ThenBy(row => row.Id, StringComparer.Ordinal).ToArray();
        var (profiles, returning) = await persistence.ReadReportProfilesAsync(rows.Select(row => row.Id).ToArray(), start, ct);
        return CalculateReport(from, to, rows, services, profiles, returning);
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
        foreach (var row in rows)
        {
            var clippedStart = row.StartUtc < start ? start : row.StartUtc;
            var local = DashboardMetrics.Local(clippedStart);
            volume[from == to ? Hour(local.Hour) : Day(DateOnly.FromDateTime(local))]++;
            if (!DashboardMetrics.BookedStatuses.Contains(row.Status)) { continue; }
            var clippedEnd = row.EndUtc > end ? end : row.EndUtc;
            var bays = row.ResourceIds.Distinct().Count();
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
