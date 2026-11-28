using MongoDB.Driver;

namespace HoleInHub.Data.Reservations;

public sealed record OutcomeRow(DateTime StartUtc, DateTime DeadlineUtc, string Status, bool WholeShop, bool PreviouslyConfirmed);

public sealed partial class ReservationPersistence
{
    public Task<List<OutcomeRow>> ReadOutcomeRowsAsync(DateTime start, DateTime end, string? service, CancellationToken ct) =>
        Reservations.Find(x => (service == null || service == "" || x.Service == service) &&
                ((x.StartUtc >= start && x.StartUtc < end) || (x.ReviewDeadlineUtc >= start && x.ReviewDeadlineUtc < end)))
            .Project(x => new OutcomeRow(x.StartUtc, x.ReviewDeadlineUtc, x.Status, x.Service == "WholeShop",
                x.Status == "Cancelled" && x.History.Any(h => h.Status == "Confirmed" || h.ReservationStatus == "Confirmed" || h.PreviousReservationStatus == "Confirmed")))
            .Limit(20001).ToListAsync(ct);

    public Task<long> CountUnmarkedAsync(DateTime today, DateTime now, CancellationToken ct) =>
        Reservations.CountDocumentsAsync(x => x.Status == "Confirmed" && x.StartUtc >= today && x.StartUtc <= now && x.CheckedInUtc == null, cancellationToken: ct);

    public Task<long> CountExpiringSoonAsync(DateTime now, DateTime until, CancellationToken ct) =>
        Reservations.CountDocumentsAsync(x => x.Status == "Pending" && x.ReviewDeadlineUtc > now && x.ReviewDeadlineUtc <= until, cancellationToken: ct);
}
