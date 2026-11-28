using HoleInHub.Data.Reservations;

namespace HoleInHub.Business.Reservations;

public readonly record struct OutcomeRate(int Count, int Eligible)
{
    public string Display => Eligible == 0 ? "—" : $"{(decimal)Count / Eligible:0.#%}";
}

public sealed record BookingOutcomes(OutcomeRate NoShows, OutcomeRate Expired, OutcomeRate Cancelled, OutcomeRate Ordinary, OutcomeRate Deposits)
{
    public static BookingOutcomes ForPeriod(IEnumerable<OutcomeRow> rows, DateOnly from, DateOnly to)
    {
        var start = DashboardMetrics.Utc(from);
        var end = DashboardMetrics.Utc(to.AddDays(1));
        int missed = 0, attended = 0, confirmed = 0, cancelled = 0, ordinary = 0, expired = 0, deposits = 0, lapsed = 0;
        foreach (var row in rows)
        {
            if (row.StartUtc >= start && row.StartUtc < end)
            {
                if (row.Status == "No-show") { missed++; }
                if (row.Status is "Completed" or "Checked-In" or AttendanceRules.AwaitingSettlement) { attended++; }
                if (row.Status == "No-show" || DashboardMetrics.BookedStatuses.Contains(row.Status) || row.PreviouslyConfirmed) { confirmed++; }
                if (row.Status == "Cancelled" && row.PreviouslyConfirmed) { cancelled++; }
            }
            if (row.DeadlineUtc < start || row.DeadlineUtc >= end) { continue; }
            if (row.WholeShop) { deposits++; if (row.Status == "Expired") { lapsed++; } }
            else { ordinary++; if (row.Status == "Expired") { expired++; } }
        }
        return new(new(missed, attended + missed), new(expired + lapsed, ordinary + deposits), new(cancelled, confirmed),
            new(expired, ordinary), new(lapsed, deposits));
    }
}

public sealed record OutcomeWeek(DateOnly From, DateOnly To, BookingOutcomes Outcomes);
