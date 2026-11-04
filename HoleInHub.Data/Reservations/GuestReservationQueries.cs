using MongoDB.Driver;

namespace HoleInHub.Data.Reservations;

public sealed record GuestReservationRecords(IReadOnlyList<ReservationRecord> Items, long UpcomingCount, long PastCount, bool HasMore);

public sealed partial class ReservationPersistence
{
    public static Collation GuestEmailCollation { get; } = new("en", strength: CollationStrength.Secondary);
    private static FilterDefinition<ReservationRecord> GuestEmail(string email) =>
        Builders<ReservationRecord>.Filter.Eq(x => x.Email, email) &
        Builders<ReservationRecord>.Filter.Regex(x => x.AccountId, "^guest:") &
        Builders<ReservationRecord>.Filter.Ne(x => x.EmailVerificationId, null) &
        Builders<ReservationRecord>.Filter.Ne(x => x.EmailVerificationId, "");

    public Task<ReservationRecord?> GuestDetailAsync(string email, string id, CancellationToken ct) =>
        Reservations.Find(GuestEmail(email) & Builders<ReservationRecord>.Filter.Eq(x => x.Id, id),
            new FindOptions { Collation = GuestEmailCollation }).FirstOrDefaultAsync(ct)!;

    public async Task<GuestReservationRecords> GuestHistoryAsync(string email, bool past, DateTime now, int page, CancellationToken ct)
    {
        var filter = GuestEmail(email);
        var upcoming = Builders<ReservationRecord>.Filter.Gt(x => x.EndUtc, now) &
            Builders<ReservationRecord>.Filter.In(x => x.Status, new[] { "Pending", "Confirmed", "Checked-In" });
        var options = new CountOptions { Collation = GuestEmailCollation };
        var upcomingCount = await Reservations.CountDocumentsAsync(filter & upcoming, options, ct);
        var pastCount = await Reservations.CountDocumentsAsync(filter & !upcoming, options, ct);
        var sort = past ? Builders<ReservationRecord>.Sort.Descending(x => x.StartUtc).Descending(x => x.Id)
            : Builders<ReservationRecord>.Sort.Ascending(x => x.StartUtc).Ascending(x => x.Id);
        var items = await Reservations.Find(filter & (past ? !upcoming : upcoming), new FindOptions { Collation = GuestEmailCollation })
            .Sort(sort)
            .Skip((Math.Clamp(page, 1, int.MaxValue / 25) - 1) * 25).Limit(26).ToListAsync(ct);
        return new(items.Take(25).ToArray(), upcomingCount, pastCount, items.Count > 25);
    }
}
