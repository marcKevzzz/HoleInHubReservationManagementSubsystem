using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;

namespace HoleInHub.Data.Customers;

public sealed partial class CustomerProfilePersistence
{
    public const int MaximumMergeBookings = 1000;
    private static CustomerProfileRecord Copy(CustomerProfileRecord profile) => BsonSerializer.Deserialize<CustomerProfileRecord>(profile.ToBson());
    private static string Hash(object payload) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload))));
    private static CustomerProfileConflictException Changed() => new("These customer records changed. Reload the comparison and review the bookings again.");
    private async Task<CustomerProfileOperationRecord?> PriorAsync(string id, string hash, IClientSessionHandle session, CancellationToken ct)
    {
        var prior = await Operations.Find(session, x => x.Id == id).FirstOrDefaultAsync(ct);
        if (prior is not null && prior.PayloadHash != hash) { throw new CustomerProfileConflictException("This operation key was already used for a different change."); }
        return prior;
    }
    public async Task<CustomerProfileOperationRecord> MergeAsync(CustomerMergeWrite command, CancellationToken ct)
    {
        var hash = Hash(new
        {
            command.SourceId,
            command.TargetId,
            command.SourceContactRevision,
            command.SourceMembershipRevision,
            command.TargetContactRevision,
            command.TargetMembershipRevision,
            ReservationIds = command.ReservationIds?.Order().ToArray(),
            command.NameFrom,
            command.EmailFrom,
            command.PhoneFrom,
            command.Reason,
            command.ActorId
        });
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(20));
        using var session = await context.Client.StartSessionAsync(cancellationToken: timeout.Token);
        return await session.WithTransactionAsync(async (s, token) =>
        {
            await GuardAsync(s, token);
            var prior = await PriorAsync(command.Id, hash, s, token); if (prior is not null) { return prior; }
            var source = await Profiles.Find(s, x => x.Id == command.SourceId).FirstOrDefaultAsync(token);
            var target = await Profiles.Find(s, x => x.Id == command.TargetId).FirstOrDefaultAsync(token);
            if (source is null || target is null || source.Id == target.Id || source.MergedIntoId is not null || target.MergedIntoId is not null
                || source.ContactRevision != command.SourceContactRevision || source.MembershipRevision != command.SourceMembershipRevision
                || target.ContactRevision != command.TargetContactRevision || target.MembershipRevision != command.TargetMembershipRevision) { throw Changed(); }
            var originalMemberships = await Memberships.Find(s, x => x.ProfileId == source.Id).ToListAsync(token);
            var all = originalMemberships.Select(x => x.Id).ToList();
            var selected = command.ReservationIds ?? all.ToArray();
            if (selected.Length is 0 or > MaximumMergeBookings || selected.Distinct().Count() != selected.Length || selected.Except(all).Any())
            { throw new CustomerProfileConflictException("Choose between 1 and 1,000 bookings belonging to the source profile."); }
            var operation = new CustomerProfileOperationRecord
            {
                Id = command.Id,
                PayloadHash = hash,
                SourceId = source.Id,
                TargetId = target.Id,
                ActorId = command.ActorId,
                ActorLabel = command.ActorLabel,
                Reason = command.Reason,
                OccurredUtc = command.OccurredUtc,
                ReservationIds = selected,
                SourceBefore = Copy(source),
                TargetBefore = Copy(target),
                MembershipsBefore = originalMemberships.Where(m => selected.Contains(m.Id)).ToArray()
            };
            var moved = await Reservations.Find(s, Builders<HoleInHub.Data.Reservations.ReservationRecord>.Filter.In(x => x.Id, selected)).ToListAsync(token);
            if (moved.Count != selected.Length) { throw Changed(); }
            var name = command.NameFrom == "source" ? source : target;
            var phone = command.PhoneFrom == "source" ? source : target;
            var email = command.EmailFrom == "source" ? source : target;
            target.FirstName = name.FirstName; target.LastName = name.LastName; target.Phone = phone.Phone; target.Email = email.Email;
            source.LastChangeOperationId = target.LastChangeOperationId = command.Id;
            target.PreferredContactPinned = true;
            target.Kind = "Combined";
            target.Variants = Variants(target.Variants.Concat(moved.Select(Contact)));
            target.Sources = target.Sources.Concat(source.Sources).Distinct().ToArray();
            target.ContactRevision++; target.MembershipRevision++; target.UpdatedUtc = command.OccurredUtc;
            source.ContactRevision++; source.MembershipRevision++; source.UpdatedUtc = command.OccurredUtc;
            source.MergedIntoId = selected.Length == all.Count ? target.Id : null;
            if (source.MergedIntoId is null)
            {
                var remaining = await Reservations.Find(s, Builders<HoleInHub.Data.Reservations.ReservationRecord>.Filter.In(x => x.Id, all.Except(selected)))
                    .SortByDescending(x => x.CreatedUtc).ThenByDescending(x => x.Id).ToListAsync(token);
                source.RepresentativeBookingId = remaining[0].Id;
                if (!source.PreferredContactPinned)
                {
                    source.FirstName = remaining[0].FirstName; source.LastName = remaining[0].LastName;
                    source.Email = remaining[0].Email; source.Phone = remaining[0].Phone;
                    source.Variants = Variants(remaining.Select(Contact));
                    SetSearchNames(source);
                }
            }
            SetSearchNames(target);
            operation.SourceContactAfter = source.ContactRevision; operation.SourceMembershipAfter = source.MembershipRevision;
            operation.TargetContactAfter = target.ContactRevision; operation.TargetMembershipAfter = target.MembershipRevision;
            operation.SourceAfterHash = ProfileStateHash(source, all.Except(selected));
            var targetIds = await Memberships.Find(s, x => x.ProfileId == target.Id).Project(x => x.Id).ToListAsync(token);
            operation.TargetAfterHash = ProfileStateHash(target, targetIds.Concat(selected));
            if (operation.ToBson().Length > 8_000_000) { throw new CustomerProfileConflictException("This comparison is too large for an interactive merge. Ask an administrator to review it."); }
            await Memberships.UpdateManyAsync(s, Builders<CustomerMembershipRecord>.Filter.In(x => x.Id, selected), Builders<CustomerMembershipRecord>.Update
                .Set(x => x.ProfileId, target.Id).Set(x => x.LastOperationId, command.Id).Inc(x => x.Revision, 1), cancellationToken: token);
            await Profiles.ReplaceOneAsync(s, x => x.Id == source.Id, source, cancellationToken: token);
            await Profiles.ReplaceOneAsync(s, x => x.Id == target.Id, target, cancellationToken: token);
            await Operations.InsertOneAsync(s, operation, cancellationToken: token);
            return operation;
        }, new TransactionOptions(ReadConcern.Snapshot, writeConcern: WriteConcern.WMajority), timeout.Token);
    }
    public async Task<CustomerProfileOperationRecord> UndoAsync(string operationId, string commandId, string reason, string actorId, string actorLabel, DateTime atUtc, CancellationToken ct)
    {
        var hash = Hash(new { operationId, reason, actorId });
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(20));
        using var session = await context.Client.StartSessionAsync(cancellationToken: timeout.Token);
        return await session.WithTransactionAsync(async (s, token) =>
        {
            await GuardAsync(s, token);
            var prior = await PriorAsync(commandId, hash, s, token); if (prior is not null) { return prior; }
            var operation = await Operations.Find(s, x => x.Id == operationId).FirstOrDefaultAsync(token);
            if (operation is null || operation.Kind != "Merge" || operation.UndoneById is not null) { throw new CustomerProfileConflictException("This merge is unavailable or has already been undone."); }
            var source = await Profiles.Find(s, x => x.Id == operation.SourceId).FirstOrDefaultAsync(token);
            var target = await Profiles.Find(s, x => x.Id == operation.TargetId).FirstOrDefaultAsync(token);
            if (source is null || target is null || !await MatchesOperationResultAsync(operation, source, target, s, token))
            { throw new CustomerProfileConflictException("Later profile changes prevent this undo. Reverse dependent operations in merge history first, or review new booking links."); }
            var membership = Builders<CustomerMembershipRecord>.Filter.In(x => x.Id, operation.ReservationIds)
                & Builders<CustomerMembershipRecord>.Filter.Eq(x => x.ProfileId, target.Id) & Builders<CustomerMembershipRecord>.Filter.Eq(x => x.LastOperationId, operation.Id);
            if (await Memberships.CountDocumentsAsync(s, membership, cancellationToken: token) != operation.ReservationIds.Length) { throw Changed(); }
            var restoredSource = Copy(operation.SourceBefore); var restoredTarget = Copy(operation.TargetBefore);
            restoredSource.ContactRevision = source.ContactRevision + 1; restoredSource.MembershipRevision = source.MembershipRevision + 1;
            restoredTarget.ContactRevision = target.ContactRevision + 1; restoredTarget.MembershipRevision = target.MembershipRevision + 1;
            restoredSource.UpdatedUtc = restoredTarget.UpdatedUtc = atUtc;
            var undo = new CustomerProfileOperationRecord
            {
                Id = commandId,
                Kind = "Undo",
                PayloadHash = hash,
                ReversesId = operation.Id,
                SourceId = source.Id,
                TargetId = target.Id,
                ActorId = actorId,
                ActorLabel = actorLabel,
                Reason = reason,
                OccurredUtc = atUtc,
                ReservationIds = operation.ReservationIds,
                SourceBefore = Copy(source),
                TargetBefore = Copy(target),
                SourceContactAfter = restoredSource.ContactRevision,
                SourceMembershipAfter = restoredSource.MembershipRevision,
                TargetContactAfter = restoredTarget.ContactRevision,
                TargetMembershipAfter = restoredTarget.MembershipRevision
            };
            if (undo.ToBson().Length > 8_000_000) { throw new CustomerProfileConflictException("This reversal needs an administrator to review its size before making changes."); }
            var previous = operation.MembershipsBefore.ToDictionary(m => m.Id);
            var restore = operation.ReservationIds.Select(id => new UpdateOneModel<CustomerMembershipRecord>(
                Builders<CustomerMembershipRecord>.Filter.Eq(x => x.Id, id) & Builders<CustomerMembershipRecord>.Filter.Eq(x => x.ProfileId, target.Id)
                    & Builders<CustomerMembershipRecord>.Filter.Eq(x => x.LastOperationId, operation.Id),
                Builders<CustomerMembershipRecord>.Update.Set(x => x.ProfileId, source.Id).Set(x => x.LastOperationId, previous[id].LastOperationId).Inc(x => x.Revision, 1)));
            await Memberships.BulkWriteAsync(s, restore, cancellationToken: token);
            await Profiles.ReplaceOneAsync(s, x => x.Id == source.Id, restoredSource, cancellationToken: token);
            await Profiles.ReplaceOneAsync(s, x => x.Id == target.Id, restoredTarget, cancellationToken: token);
            await Operations.UpdateOneAsync(s, x => x.Id == operation.Id, Builders<CustomerProfileOperationRecord>.Update.Set(x => x.UndoneById, commandId), cancellationToken: token);
            await Operations.InsertOneAsync(s, undo, cancellationToken: token);
            return undo;
        }, new TransactionOptions(ReadConcern.Snapshot, writeConcern: WriteConcern.WMajority), timeout.Token);
    }

    private static string ProfileStateHash(CustomerProfileRecord profile, IEnumerable<string> ids) => Hash(new
    {
        profile.Id,
        profile.FirstName,
        profile.LastName,
        profile.Email,
        profile.Phone,
        profile.Kind,
        profile.RepresentativeBookingId,
        profile.MergedIntoId,
        profile.PreferredContactPinned,
        profile.LastChangeOperationId,
        Sources = profile.Sources.Order(StringComparer.Ordinal).ToArray(),
        SearchNames = profile.SearchNames.Order(StringComparer.Ordinal).ToArray(),
        Variants = profile.Variants.OrderBy(x => x.BookingId, StringComparer.Ordinal).ThenBy(x => x.FirstName, StringComparer.Ordinal).ThenBy(x => x.Phone, StringComparer.Ordinal).ToArray(),
        MembershipIds = ids.Order(StringComparer.Ordinal).ToArray()
    });
    private async Task<bool> MatchesOperationResultAsync(CustomerProfileOperationRecord operation, CustomerProfileRecord source,
        CustomerProfileRecord target, IClientSessionHandle? session, CancellationToken ct)
    {
        if (source.LastChangeOperationId != operation.Id || target.LastChangeOperationId != operation.Id || operation.MembershipsBefore.Length != operation.ReservationIds.Length)
        { return false; }
        var filter = Builders<CustomerMembershipRecord>.Filter.In(x => x.ProfileId, new[] { source.Id, target.Id });
        var memberships = await (session is null ? Memberships.Find(filter) : Memberships.Find(session, filter)).ToListAsync(ct);
        return ProfileStateHash(source, memberships.Where(x => x.ProfileId == source.Id).Select(x => x.Id)) == operation.SourceAfterHash
            && ProfileStateHash(target, memberships.Where(x => x.ProfileId == target.Id).Select(x => x.Id)) == operation.TargetAfterHash
            && operation.ReservationIds.All(id => memberships.Any(m => m.Id == id && m.ProfileId == target.Id && m.LastOperationId == operation.Id));
    }
}
