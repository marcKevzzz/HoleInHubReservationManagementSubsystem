using HoleInHub.Data.Reservations;

namespace HoleInHub.Business.Reservations;

public sealed record CoachDirectoryEntry(CoachDto Coach, CoachFreePeriod? NextFreePeriod);
public sealed record CoachFreePeriod(DateTime StartUtc, DateTime EndUtc, bool FreeNow);

public sealed partial class ReservationService
{
    public const int CoachDirectoryDays = 30;

    public async Task<IReadOnlyList<CoachDirectoryEntry>> CoachDirectoryAsync(CancellationToken ct)
    {
        var now = (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime;
        var coaches = await persistence.ReadCoachesAsync(ct);
        var firstDay = DateOnly.FromDateTime(DashboardMetrics.Local(now));
        var end = DashboardMetrics.Utc(firstDay.AddDays(CoachDirectoryDays));
        var assignments = (await persistence.ReadCoachAssignmentsAsync(now, end, ct)).ToLookup(x => x.CoachId);
        return coaches.Select(coach => new CoachDirectoryEntry(MapCoach(coach),
            NextCoachFreePeriod(coach, now, assignments[coach.Id]))).ToArray();
    }

    public static CoachFreePeriod? NextCoachFreePeriod(CoachRecord coach, DateTime now, IEnumerable<CoachAssignmentInterval> assignments)
    {
        if (!coach.Active) { return null; }
        var firstDay = DateOnly.FromDateTime(DashboardMetrics.Local(now));
        var shifts = new List<(DateTime Start, DateTime End)>();
        for (var offset = 0; offset < CoachDirectoryDays; offset++)
        {
            var day = firstDay.AddDays(offset);
            var midnight = DashboardMetrics.Utc(day);
            foreach (var (startMinute, endMinute) in coach.WindowsOn(day))
            {
                var start = midnight.AddMinutes(startMinute);
                var end = midnight.AddMinutes(endMinute);
                if (end <= now) { continue; }
                if (shifts.Count > 0 && shifts[^1].End >= start)
                {
                    var last = shifts[^1];
                    if (end > last.End) { shifts[^1] = (last.Start, end); }
                }
                else { shifts.Add((start < now ? now : start, end)); }
            }
        }

        var holds = assignments.Where(x => x.CoachId == coach.Id).OrderBy(x => x.StartUtc).ToArray();
        var index = 0;
        foreach (var (start, end) in shifts)
        {
            var cursor = start;
            while (index < holds.Length)
            {
                var hold = holds[index];
                if (hold.StartUtc >= end) { break; }
                if (hold.StartUtc > cursor) { return new(cursor, hold.StartUtc, cursor == now); }
                if (hold.EndUtc > cursor) { cursor = hold.EndUtc; }
                if (cursor >= end) { break; }
                index++;
            }
            if (cursor < end) { return new(cursor, end, cursor == now); }
        }
        return null;
    }
}
