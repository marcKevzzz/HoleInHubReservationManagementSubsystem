using MongoDB.Driver;

namespace HoleInHub.Data.Reservations;

public sealed class DashboardRow
{
    public string Id { get; set; } = "";
    public string Reference { get; set; } = "";
    public string Guest { get; set; } = "";
    public string Service { get; set; } = "";
    public string Status { get; set; } = "";
    public DateTime StartUtc { get; set; }
    public DateTime EndUtc { get; set; }
    public string[] ResourceIds { get; set; } = [];
    public bool CancellationRequested { get; set; }
    public DateTime? CheckedInUtc { get; set; }
}

public sealed partial class ReservationPersistence
{
    private static readonly System.Linq.Expressions.Expression<Func<ReservationRecord, DashboardRow>> DashboardProjection = x => new DashboardRow
    {
        Id = x.Id, Reference = x.Reference, Guest = (x.FirstName ?? "") + " " + (x.LastName ?? ""),
        Service = x.Service, Status = x.Status, StartUtc = x.StartUtc, EndUtc = x.EndUtc,
        ResourceIds = x.ResourceIds, CancellationRequested = x.CancellationRequested, CheckedInUtc = x.CheckedInUtc
    };

    // Explicit bound: callers reject an oversized range rather than reporting truncated totals.
    public Task<List<DashboardRow>> ReadDashboardRowsAsync(DateTime start, DateTime end, CancellationToken ct) =>
        Reservations.Find(x => x.StartUtc < end && x.EndUtc > start).Project(DashboardProjection).Limit(20001).ToListAsync(ct);

    public Task<List<DashboardRow>> ReadDashboardSliceAsync(DateTime start, DateTime end, string? service,
        string[]? statuses, bool excludeStatuses, (DateTime Start, DateTime End)[] intervals, bool byStart,
        int page, CancellationToken ct)
    {
        var f = Builders<ReservationRecord>.Filter;
        var filter = f.Lt(x => x.StartUtc, end) & f.Gt(x => x.EndUtc, start);
        if (service is not null)
        {
            filter &= f.Eq(x => x.Service, service);
        }

        if (statuses is not null)
        {
            filter &= excludeStatuses ? f.Nin(x => x.Status, statuses) : f.In(x => x.Status, statuses);
        }

        if (intervals.Length > 0)
        {
            filter &= f.Or(intervals.Select(interval => byStart
                ? f.Gte(x => x.StartUtc, interval.Start) & f.Lt(x => x.StartUtc, interval.End)
                : f.Lt(x => x.StartUtc, interval.End) & f.Gt(x => x.EndUtc, interval.Start)));
        }

        return Reservations.Find(filter).SortBy(x => x.StartUtc).ThenBy(x => x.Id)
            .Skip((page - 1) * 20).Limit(21).Project(DashboardProjection).ToListAsync(ct);
    }

    public async Task<(List<ResourceRecord> Resources, List<DashboardRow> Bookings, List<ScheduleBlock> Blocks)> ReadDashboardLiveAsync(DateTime now, CancellationToken ct)
    {
        var resources = Resources.Find(x => x.Active).SortBy(x => x.Id).ToListAsync(ct);
        var bookings = Reservations.Find(HoldingCapacity(now) & Builders<ReservationRecord>.Filter.Where(x => x.StartUtc <= now && x.EndUtc > now))
            .Project(DashboardProjection).ToListAsync(ct);
        var blocks = context.Database.GetCollection<ScheduleBlock>("schedule_blocks")
            .Find(x => x.ReleasedUtc == null && x.StartUtc <= now && x.EndUtc > now).ToListAsync(ct);
        await Task.WhenAll(resources, bookings, blocks);
        return (await resources, await bookings, await blocks);
    }
}
