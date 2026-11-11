using System.Text.RegularExpressions;
using HoleInHub.Data.Reservations;
using MongoDB.Bson;
using MongoDB.Driver;

namespace HoleInHub.Data.Customers;

public sealed partial class CustomerProfilePersistence
{
    public async Task<CustomerDirectoryRecords> SearchAsync(string term, string[] phones, int page, CancellationToken ct)
    {
        var f = Builders<CustomerProfileRecord>.Filter;
        var filter = f.Eq(x => x.MergedIntoId, null);
        if (term.Length > 0)
        {
            var literal = new BsonRegularExpression(Regex.Escape(term), "i");
            var search = f.Regex("SearchNames", literal) | f.Regex(x => x.Email, literal) | f.Regex("Variants.Email", literal);
            if (phones.Length > 0) { search |= f.In(x => x.Phone, phones) | f.In("Variants.Phone", phones); }
            var booking = await Reservations.Find(x => x.Reference == term.ToUpperInvariant()).Project(x => x.Id).FirstOrDefaultAsync(ct);
            if (booking is not null)
            {
                var membership = await Memberships.Find(x => x.Id == booking).FirstOrDefaultAsync(ct);
                if (membership is not null) { search |= f.Eq(x => x.Id, membership.ProfileId); }
            }
            filter &= search;
        }
        var profiles = await Profiles.Find(filter, new FindOptions { MaxTime = TimeSpan.FromSeconds(5) }).SortByDescending(x => x.UpdatedUtc).ThenBy(x => x.Id)
            .Skip((page - 1) * 25).Limit(26).ToListAsync(ct);
        var ids = profiles.Take(25).Select(x => x.RepresentativeBookingId).ToArray();
        var records = await Reservations.Find(Builders<ReservationRecord>.Filter.In(x => x.Id, ids)).ToListAsync(ct);
        var items = new List<CustomerContactRecord>();
        foreach (var profile in profiles.Take(25))
        {
            var record = records.FirstOrDefault(r => r.Id == profile.RepresentativeBookingId);
            if (record is null) { continue; }
            SetDisplayContact(record, profile);
            items.Add(new(record, profile.Kind));
        }
        return new(items, profiles.Count > 25);
    }
    private static void SetDisplayContact(ReservationRecord record, CustomerProfileRecord profile)
    {
        record.FirstName = profile.FirstName; record.LastName = profile.LastName; record.Email = profile.Email; record.Phone = profile.Phone;
    }
    public async Task<CustomerProfileRecords?> ReadHistoryAsync(string key, bool upcomingOnly, int page, DateTime now, CancellationToken ct)
    {
        var snapshot = await ReadAsync(key, ct);
        if (snapshot is null) { return null; }
        // shortcut: materialize membership IDs for the venue directory; switch to an indexed lookup pipeline if measured history volume exceeds the query budget.
        var filter = Builders<ReservationRecord>.Filter.In(x => x.Id, snapshot.ReservationIds);
        var upcoming = filter & Builders<ReservationRecord>.Filter.Where(x => x.EndUtc > now && (x.Status == "Pending" || x.Status == "Confirmed" || x.Status == "Checked-In"));
        var total = await Reservations.CountDocumentsAsync(filter, cancellationToken: ct);
        var upcomingCount = await Reservations.CountDocumentsAsync(upcoming, cancellationToken: ct);
        var completed = await Reservations.CountDocumentsAsync(filter & Builders<ReservationRecord>.Filter.Eq(x => x.Status, "Completed"), cancellationToken: ct);
        var cancelled = await Reservations.CountDocumentsAsync(filter & Builders<ReservationRecord>.Filter.Eq(x => x.Status, "Cancelled"), cancellationToken: ct);
        var representative = await Reservations.Find(x => x.Id == snapshot.Profile.RepresentativeBookingId).FirstOrDefaultAsync(ct);
        if (representative is null) { return null; }
        SetDisplayContact(representative, snapshot.Profile);
        var records = await Reservations.Find(upcomingOnly ? upcoming : filter).Sort(upcomingOnly
                ? Builders<ReservationRecord>.Sort.Ascending(x => x.StartUtc).Ascending(x => x.Id)
                : Builders<ReservationRecord>.Sort.Descending(x => x.StartUtc).Descending(x => x.Id))
            .Skip((page - 1) * 25).Limit(26).ToListAsync(ct);
        return new(new(representative, snapshot.Profile.Kind), records.Take(25).ToArray(), total, upcomingCount, completed, cancelled, records.Count > 25);
    }
    public Task<List<ReservationRecord>> BookingsAsync(string[] ids, CancellationToken ct) => Reservations
        .Find(Builders<ReservationRecord>.Filter.In(x => x.Id, ids)).SortByDescending(x => x.StartUtc).ThenBy(x => x.Id).Limit(MaximumMergeBookings).ToListAsync(ct);
    public async Task<(DateTime? First, DateTime? Last)> DateRangeAsync(string[] ids, CancellationToken ct)
    {
        var filter = Builders<ReservationRecord>.Filter.In(x => x.Id, ids);
        var first = await Reservations.Find(filter).SortBy(x => x.StartUtc).Project(x => (DateTime?)x.StartUtc).FirstOrDefaultAsync(ct);
        var last = await Reservations.Find(filter).SortByDescending(x => x.StartUtc).Project(x => (DateTime?)x.StartUtc).FirstOrDefaultAsync(ct);
        return (first, last);
    }
}
