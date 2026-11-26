using MongoDB.Driver;

namespace HoleInHub.Data.Reservations;

public sealed record OccupiedInterval(DateTime StartUtc, DateTime EndUtc);

public sealed record BayOccupancy(DateTime StartUtc, DateTime EndUtc, string[] ResourceIds);

public sealed partial class ReservationPersistence
{
    public async Task<IReadOnlyList<BayOccupancy>> ReadBayOccupancyAsync(DateTime start, DateTime end, DateTime now, CancellationToken ct)
    {
        var bookings = Reservations.Find(HoldingCapacity(now) & Builders<ReservationRecord>.Filter.Where(x => x.ClassSessionId == null && x.StartUtc < end && x.EndUtc > start))
            .Project(x => new BayOccupancy(x.StartUtc, x.EndUtc, x.ResourceIds)).ToListAsync(ct);
        var blocks = Blocks.Find(x => x.ReleasedUtc == null && x.StartUtc < end && x.EndUtc > start)
            .Project(x => new BayOccupancy(x.StartUtc, x.EndUtc, x.ResourceIds)).ToListAsync(ct);
        var classes = Classes.Find(ClassAllocations(start, end))
            .Project(x => new BayOccupancy(x.StartUtc, x.EndUtc, x.ResourceIds)).ToListAsync(ct);
        return (await Task.WhenAll(bookings, blocks, classes)).SelectMany(rows => rows).ToArray();
    }

    public async Task<IReadOnlyList<OccupiedInterval>> ReadOccupiedIntervalsAsync(DateTime start, DateTime end,
        string[] activeBays, DateTime now, CancellationToken ct)
    {
        var bookings = await Reservations.Find(HoldingCapacity(now) & Builders<ReservationRecord>.Filter
            .Where(x => x.ClassSessionId == null && x.StartUtc < end && x.EndUtc > start) & Builders<ReservationRecord>.Filter.AnyIn(x => x.ResourceIds, activeBays))
            .Project(x => new OccupiedInterval(x.StartUtc, x.EndUtc)).ToListAsync(ct);
        var filter = Builders<ScheduleBlock>.Filter;
        var blocks = await context.Database.GetCollection<ScheduleBlock>("schedule_blocks")
            .Find(filter.Where(x => x.ReleasedUtc == null && x.StartUtc < end && x.EndUtc > start)
                & (filter.Size(x => x.ResourceIds, 0) | filter.AnyIn(x => x.ResourceIds, activeBays)))
            .Project(x => new OccupiedInterval(x.StartUtc, x.EndUtc)).ToListAsync(ct);
        var classes = await Classes.Find(ClassAllocations(start, end) & Builders<ClassSessionRecord>.Filter.AnyIn(x => x.ResourceIds, activeBays))
            .Project(x => new OccupiedInterval(x.StartUtc, x.EndUtc)).ToListAsync(ct);
        return bookings.Concat(blocks).Concat(classes).ToArray();
    }
}
