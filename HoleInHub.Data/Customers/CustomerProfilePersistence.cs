using HoleInHub.Data.Context;
using HoleInHub.Data.Reservations;
using MongoDB.Bson;
using MongoDB.Driver;

namespace HoleInHub.Data.Customers;

public sealed partial class CustomerProfilePersistence(IMongoDbContext context)
{
    public const string ProfilesName = "customer_profiles", MembershipsName = "customer_profile_bookings", OperationsName = "customer_profile_operations", GuardsName = "customer_profile_guards";
    private IMongoCollection<CustomerProfileRecord> Profiles => context.Database.GetCollection<CustomerProfileRecord>(ProfilesName);
    private IMongoCollection<CustomerMembershipRecord> Memberships => context.Database.GetCollection<CustomerMembershipRecord>(MembershipsName);
    private IMongoCollection<CustomerProfileOperationRecord> Operations => context.Database.GetCollection<CustomerProfileOperationRecord>(OperationsName);
    private IMongoCollection<ReservationRecord> Reservations => context.Database.GetCollection<ReservationRecord>("reservations");
    public async Task<bool> InstalledAsync(CancellationToken ct) => await context.Database.GetCollection<BsonDocument>(GuardsName).Find(new BsonDocument("_id", "installed")).AnyAsync(ct);
    private async Task GuardAsync(IClientSessionHandle session, CancellationToken ct)
    {
        // shortcut: serialize profile metadata at this venue's volume; split into ordered profile guards if measured contention grows.
        var result = await context.Database.GetCollection<BsonDocument>(GuardsName).UpdateOneAsync(session,
            new BsonDocument("_id", "memberships"), new BsonDocument("$inc", new BsonDocument("version", 1L)), cancellationToken: ct);
        if (result.MatchedCount != 1) { throw new CustomerProfileConflictException("Apply the customer-profile database update before changing profiles."); }
    }
    private async Task<CustomerProfileRecord?> ResolveAsync(string id, IClientSessionHandle? session, CancellationToken ct)
    {
        var visited = new HashSet<string>();
        while (visited.Add(id))
        {
            var profile = await (session is null ? Profiles.Find(x => x.Id == id) : Profiles.Find(session, x => x.Id == id)).FirstOrDefaultAsync(ct);
            if (profile?.MergedIntoId is null) { return profile; }
            id = profile.MergedIntoId;
        }
        throw new CustomerProfileConflictException("This profile has a conflicting consolidation history. Ask an administrator to review it.");
    }
    public async Task<CustomerProfileSnapshot?> ReadAsync(string key, CancellationToken ct)
    {
        var membership = key.StartsWith("cp_", StringComparison.Ordinal) ? null : await Memberships.Find(x => x.Id == key).FirstOrDefaultAsync(ct);
        var profile = await ResolveAsync(membership?.ProfileId ?? key, null, ct);
        if (profile is null) { return null; }
        var ids = await Memberships.Find(x => x.ProfileId == profile.Id).Project(x => x.Id).ToListAsync(ct);
        return new(profile, ids.ToArray());
    }
    private static CustomerContactVariant Contact(ReservationRecord r) => new(r.FirstName, r.LastName, r.Email, r.Phone, r.Id, r.CreatedUtc);
    private static CustomerContactVariant[] Variants(IEnumerable<CustomerContactVariant> variants) => variants.OrderByDescending(x => x.RecordedUtc)
        .DistinctBy(x => (x.FirstName, x.LastName, x.Email.ToLowerInvariant(), x.Phone)).ToArray();
    private static void SetSearchNames(CustomerProfileRecord profile) => profile.SearchNames = profile.Variants
        .Select(v => (v.FirstName + " " + v.LastName).Trim()).Append((profile.FirstName + " " + profile.LastName).Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    public async Task AttachAsync(IClientSessionHandle session, ReservationRecord record, string? selectedProfileId, CancellationToken ct, bool legacyGrouping = false)
    {
        await GuardAsync(session, ct);
        if (await Memberships.Find(session, x => x.Id == record.Id).AnyAsync(ct)) { return; }
        var (identity, kind) = CustomerProfileRecord.Identity(record, legacyGrouping);
        var profile = await ResolveAsync(selectedProfileId ?? identity, session, ct);
        if (selectedProfileId is not null && profile is null) { throw new CustomerProfileConflictException("That customer profile is unavailable. Choose it again."); }
        if (profile is null)
        {
            profile = new()
            {
                Id = identity,
                Kind = kind,
                FirstName = record.FirstName,
                LastName = record.LastName,
                Email = record.Email,
                Phone = record.Phone,
                LastChangeOperationId = "booking:" + record.Id,
                RepresentativeBookingId = record.Id,
                CreatedUtc = record.CreatedUtc,
                UpdatedUtc = record.CreatedUtc,
                Variants = [Contact(record)],
                Sources = [kind]
            };
            SetSearchNames(profile);
            await Profiles.InsertOneAsync(session, profile, cancellationToken: ct);
        }
        else
        {
            if (selectedProfileId is not null) { profile.PreferredContactPinned = true; profile.Kind = "Combined"; }
            profile.LastChangeOperationId = "booking:" + record.Id;
            profile.MembershipRevision++;
            profile.ContactRevision++;
            profile.Variants = Variants(profile.Variants.Append(Contact(record)));
            profile.Sources = profile.Sources.Append(kind).Distinct().ToArray();
            if (!profile.PreferredContactPinned && record.CreatedUtc >= profile.UpdatedUtc)
            {
                profile.FirstName = record.FirstName; profile.LastName = record.LastName; profile.Email = record.Email; profile.Phone = record.Phone;
                profile.RepresentativeBookingId = record.Id;
            }
            profile.UpdatedUtc = record.CreatedUtc > profile.UpdatedUtc ? record.CreatedUtc : profile.UpdatedUtc;
            SetSearchNames(profile);
            await Profiles.ReplaceOneAsync(session, x => x.Id == profile.Id, profile, cancellationToken: ct);
        }
        await Memberships.InsertOneAsync(session, new() { Id = record.Id, ProfileId = profile.Id }, cancellationToken: ct);
    }
    public async Task CreateContactAsync(CustomerProfileRecord profile, CancellationToken ct)
    {
        using var session = await context.Client.StartSessionAsync(cancellationToken: ct);
        await session.WithTransactionAsync(async (s, token) =>
        {
            await GuardAsync(s, token);
            var prior = await Profiles.Find(s, x => x.Id == profile.Id).FirstOrDefaultAsync(token);
            if (prior is not null)
            {
                if (prior.FirstName != profile.FirstName || prior.LastName != profile.LastName || prior.Phone != profile.Phone || prior.Email != profile.Email)
                {
                    throw new CustomerProfileConflictException("This form already added different contact details. Open Add customer again.");
                }
                return true;
            }
            SetSearchNames(profile);
            await Profiles.InsertOneAsync(s, profile, cancellationToken: token);
            return true;
        }, new TransactionOptions(ReadConcern.Snapshot, writeConcern: WriteConcern.WMajority), ct);
    }

    private static ReservationRecord ContactWithoutBooking(CustomerProfileRecord profile) => new()
    {
        Id = profile.Id,
        FirstName = profile.FirstName,
        LastName = profile.LastName,
        Phone = profile.Phone,
        Email = profile.Email,
        CreatedUtc = profile.CreatedUtc,
        Reference = "No reservations yet"
    };

    public async Task BackfillAsync(CancellationToken ct)
    {
        using var cursor = await Reservations.Find(_ => true).SortBy(x => x.CreatedUtc).ThenBy(x => x.Id).ToCursorAsync(ct);
        while (await cursor.MoveNextAsync(ct))
        {
            foreach (var record in cursor.Current)
            {
                using var session = await context.Client.StartSessionAsync(cancellationToken: ct);
                await session.WithTransactionAsync(async (s, token) => { await AttachAsync(s, record, null, token, legacyGrouping: true); return true; },
                    new TransactionOptions(ReadConcern.Snapshot, writeConcern: WriteConcern.WMajority), ct);
            }
        }
    }
    public async Task<List<CustomerProfileOperationRecord>> HistoryAsync(string profileId, int page, CancellationToken ct)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal) { profileId };
        var frontier = new[] { profileId };
        while (frontier.Length > 0)
        {
            var aliases = await Profiles.Find(Builders<CustomerProfileRecord>.Filter.In(x => x.MergedIntoId, frontier)).Project(x => x.Id).ToListAsync(ct);
            frontier = aliases.Where(ids.Add).ToArray();
        }
        var filter = Builders<CustomerProfileOperationRecord>.Filter.In(x => x.SourceId, ids) | Builders<CustomerProfileOperationRecord>.Filter.In(x => x.TargetId, ids);
        return await Operations.Find(filter).SortByDescending(x => x.OccurredUtc).ThenByDescending(x => x.Id).Skip((page - 1) * 20).Limit(21).ToListAsync(ct);
    }
    public Task<CustomerProfileOperationRecord?> OperationAsync(string id, CancellationToken ct) => Operations.Find(x => x.Id == id).FirstOrDefaultAsync(ct)!;
    public async Task<bool> CanUndoAsync(CustomerProfileOperationRecord op, CancellationToken ct)
    {
        if (op.Kind != "Merge" || op.UndoneById is not null) { return false; }
        var records = await Profiles.Find(Builders<CustomerProfileRecord>.Filter.In(x => x.Id, new[] { op.SourceId, op.TargetId })).ToListAsync(ct);
        var source = records.FirstOrDefault(x => x.Id == op.SourceId);
        var target = records.FirstOrDefault(x => x.Id == op.TargetId);
        return source is not null && target is not null && await MatchesOperationResultAsync(op, source, target, null, ct);
    }
}
