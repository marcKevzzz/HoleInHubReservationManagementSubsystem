using HoleInHub.Data.Customers;
using MongoDB.Driver;
using MongoDB.Driver.Linq;

namespace HoleInHub.Data.Reservations;

public sealed partial class ReservationPersistence
{
    public async Task<(Dictionary<string, string> Profiles, HashSet<string> Returning)> ReadReportProfilesAsync(
        string[] bookingIds, DateTime periodStart, CancellationToken ct)
    {
        var memberships = context.Database.GetCollection<CustomerMembershipRecord>(CustomerProfilePersistence.MembershipsName);
        var profiles = new Dictionary<string, string>();
        foreach (var ids in bookingIds.Chunk(1000))
        {
            var matches = await memberships.Find(x => Enumerable.Contains(ids, x.Id))
                .Project(x => new { x.Id, x.ProfileId }).ToListAsync(ct);
            foreach (var match in matches) { profiles.Add(match.Id, match.ProfileId); }
        }

        var returning = new HashSet<string>();
        foreach (var ids in profiles.Values.Distinct().Chunk(1000))
        {
            var prior = from member in memberships.AsQueryable()
                        where Enumerable.Contains(ids, member.ProfileId)
                        join booking in Reservations.AsQueryable() on member.Id equals booking.Id
                        where booking.Status == "Completed" && (booking.CheckedOutUtc ?? booking.EndUtc) <= periodStart
                        select member.ProfileId;
            returning.UnionWith(await prior.Distinct().ToListAsync(ct));
        }
        return (profiles, returning);
    }
}
