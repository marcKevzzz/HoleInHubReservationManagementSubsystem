using MongoDB.Driver;

namespace HoleInHub.Data.Reservations;

public sealed record OccupiedInterval(DateTime StartUtc, DateTime EndUtc);

public sealed partial class ReservationPersistence
{
    public async Task<IReadOnlyList<OccupiedInterval>> ReadOccupiedIntervalsAsync(DateTime start, DateTime end,
        string[] activeBays, DateTime now, CancellationToken ct)
    {
        var bookings = await Reservations.Find(HoldingCapacity(now) & Builders<ReservationRecord>.Filter
            .Where(x => x.StartUtc < end && x.EndUtc > start) & Builders<ReservationRecord>.Filter.AnyIn(x => x.ResourceIds, activeBays))
            .Project(x => new OccupiedInterval(x.StartUtc, x.EndUtc)).ToListAsync(ct);
        var filter = Builders<ScheduleBlock>.Filter;
        var blocks = await context.Database.GetCollection<ScheduleBlock>("schedule_blocks")
            .Find(filter.Where(x => x.ReleasedUtc == null && x.StartUtc < end && x.EndUtc > start)
                & (filter.Size(x => x.ResourceIds, 0) | filter.AnyIn(x => x.ResourceIds, activeBays)))
            .Project(x => new OccupiedInterval(x.StartUtc, x.EndUtc)).ToListAsync(ct);
        return bookings.Concat(blocks).ToArray();
    }
}
