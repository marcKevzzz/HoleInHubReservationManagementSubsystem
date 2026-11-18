using MongoDB.Driver;

namespace HoleInHub.Data.Reservations;

public sealed partial class ReservationPersistence
{
    public Task<List<ReservationRecord>> UpcomingRemindersAsync(DateTime now, CancellationToken ct) =>
        Reservations.Find(x => x.Status == "Confirmed" && x.CheckedInUtc == null && !x.CancellationRequested
            && x.Email != "" && x.StartUtc > now && x.StartUtc <= now.AddMinutes(15)).ToListAsync(ct);
}
