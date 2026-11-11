using HoleInHub.Data.Context;
using MongoDB.Bson;
using MongoDB.Driver;
namespace HoleInHub.Data.Reservations;

public sealed partial class ReservationPersistence(IMongoDbContext context)
{
    private IMongoCollection<ReservationRecord> Reservations => context.Database.GetCollection<ReservationRecord>("reservations");
    private IMongoCollection<ResourceRecord> Resources => context.Database.GetCollection<ResourceRecord>("resources");
    public async Task<IReadOnlyList<ResourceRecord>> CatalogAsync(CancellationToken ct) => await Resources.Find(x => x.Active).SortBy(x => x.Id).ToListAsync(ct);
    public Task<List<ResourceRecord>> ReadResourceDirectoryAsync(CancellationToken ct) =>
        Resources.Find(_ => true).SortBy(x => x.Service).ThenBy(x => x.Name).ThenBy(x => x.Id).ToListAsync(ct);
    public async Task<IReadOnlyList<ReservationRecord>> OwnedAsync(string accountId, CancellationToken ct) => await Reservations.Find(x => x.AccountId == accountId).SortByDescending(x => x.CreatedUtc).ToListAsync(ct);
    public Task<ReservationRecord?> OwnedAsync(string accountId, string id, CancellationToken ct) => Reservations.Find(x => x.Id == id && x.AccountId == accountId).FirstOrDefaultAsync(ct)!;
    public Task<ReservationRecord?> LatestOwnedAsync(string accountId, CancellationToken ct) => Reservations.Find(x => x.AccountId == accountId).SortByDescending(x => x.CreatedUtc).FirstOrDefaultAsync(ct)!;
    public Task<ReservationRecord?> ByKeyAsync(string accountId, string key, CancellationToken ct) => Reservations.Find(x => x.AccountId == accountId && x.IdempotencyKey == key).FirstOrDefaultAsync(ct)!;
    private static FilterDefinition<ReservationRecord> HoldingCapacity(DateTime now) =>
        Builders<ReservationRecord>.Filter.Where(x => x.Status == "Confirmed" || x.Status == "Checked-In" ||
            x.Status == "Pending" && (x.Service == "WholeShop" || x.ReviewDeadlineUtc > now));

    public async Task<string[]> OccupiedAsync(DateTime start, DateTime end, CancellationToken ct, DateTime? now = null) { var records = await Reservations.Find(HoldingCapacity(now ?? DateTime.UtcNow) & Builders<ReservationRecord>.Filter.Where(x => x.StartUtc < end && x.EndUtc > start)).ToListAsync(ct); var blocks = await context.Database.GetCollection<ScheduleBlock>("schedule_blocks").Find(x => x.ReleasedUtc == null && x.StartUtc < end && x.EndUtc > start).ToListAsync(ct); var catalog = await CatalogAsync(ct); return records.SelectMany(x => x.ResourceIds).Concat(blocks.SelectMany(x => x.ResourceIds.Length == 0 ? catalog.Select(r => r.Id) : x.ResourceIds)).Distinct().ToArray(); }
    private async Task GuardAsync(IClientSessionHandle session, ReservationRecord record, CancellationToken ct)
    {
        var guards = context.Database.GetCollection<BsonDocument>("allocation_guards"); var day = TimeZoneInfo.ConvertTimeFromUtc(record.StartUtc, TimeZoneInfo.FindSystemTimeZoneById("Asia/Manila")).ToString("yyyy-MM-dd"); foreach (var id in new[] { "catalog" }.Concat(record.ResourceIds.Order().Select(x => $"{x}:{day}")))
        {
            await guards.UpdateOneAsync(session, new BsonDocument("_id", id), new BsonDocument("$inc", new BsonDocument("version", 1)), new UpdateOptions { IsUpsert = true }, ct);
        }
    }

    private Task GuardCatalogAsync(IClientSessionHandle session, CancellationToken ct) =>
        context.Database.GetCollection<BsonDocument>("allocation_guards").UpdateOneAsync(session, new BsonDocument("_id", "catalog"),
            new BsonDocument("$inc", new BsonDocument("version", 1)), new UpdateOptions { IsUpsert = true }, ct);
    public async Task<ReservationRecord> InsertAsync(ReservationRecord record, CancellationToken ct, string? encryptedReceipt = null, string? customerProfileId = null)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(15)); ct = timeout.Token;
        using var session = await context.Client.StartSessionAsync(cancellationToken: ct);
        try
        {
            return await session.WithTransactionAsync(async (s, token) =>
            {
                await GuardAsync(s, record, token);
                var prior = await Reservations.Find(s, x => x.AccountId == record.AccountId && x.IdempotencyKey == record.IdempotencyKey).FirstOrDefaultAsync(token); if (prior != null)
                {
                    return prior;
                }

                var catalog = await Resources.Find(s, x => x.Active).ToListAsync(token);
                if (record.Service == "WholeShop")
                {
                    if (record.PolicyVersion >= 2 && !record.ResourceIds.Order().SequenceEqual(catalog.Select(x => x.Id).Order()))
                    {
                        throw new AllocationConflictException("The bay catalog changed. Reload and select the whole-shop session again.");
                    }
                    record.ResourceIds = catalog.Select(x => x.Id).Order().ToArray();
                    await GuardAsync(s, record, token);
                }

                if (record.ResourceIds.Length == 0 || record.ResourceIds.Any(id => !catalog.Any(r => r.Id == id && (record.Service == "WholeShop" || r.Service == (record.Service == "VIP" ? "VIP" : "Regular")))))
                {
                    throw new AllocationConflictException("One or more selected resources are unavailable for this service.");
                }

                if (record.PolicyVersion == 1 && !record.Participants.Select(x => x.ResourceId).Order().SequenceEqual(record.ResourceIds.Order()))
                {
                    throw new AllocationConflictException("The bay catalog changed. Reload the form and check participant counts for every selected bay.");
                }
                record.ResourceNames = catalog.Where(x => record.ResourceIds.Contains(x.Id)).ToDictionary(x => x.Id, x => x.Name);
                if (await HasAllocationConflictAsync(record, s, token, record.CreatedUtc == default ? DateTime.UtcNow : record.CreatedUtc))
                {
                    throw new AllocationConflictException("These bays are no longer available. Please choose another time or bay.");
                }

                if (record.EmailVerificationId != null)
                {
                    var verified = await context.Database.GetCollection<GuestVerificationRecord>("guest_verifications").UpdateOneAsync(s,
                        x => x.Id == record.EmailVerificationId && x.Email == record.Email && x.VerifiedUtc != null && x.ExpiresUtc > record.CreatedUtc && x.ConsumedBySubmissionKey == null,
                        Builders<GuestVerificationRecord>.Update.Set(x => x.ConsumedBySubmissionKey, record.IdempotencyKey), cancellationToken: token);
                    if (verified.ModifiedCount != 1)
                    {
                        throw new AllocationConflictException("Verify your email again before submitting another booking.");
                    }
                }
                record.CustomerAccountId = record.AccountId.StartsWith("guest:", StringComparison.Ordinal)
                    || record.AccountId.StartsWith("staff:", StringComparison.Ordinal) ? null : record.AccountId;
                await Reservations.InsertOneAsync(s, record, cancellationToken: token);
                var profiles = new HoleInHub.Data.Customers.CustomerProfilePersistence(context);
                if (await profiles.InstalledAsync(token))
                {
                    await profiles.AttachAsync(s, record, customerProfileId, token);
                }
                else if (customerProfileId is not null)
                {
                    throw new HoleInHub.Data.Customers.CustomerProfileConflictException("Apply the customer-profile database update before selecting a profile.");
                }

                await NotifyAsync(s, record, "submitted", "Reservation request received", token, encryptedReceipt);
                return record;
            }, new TransactionOptions(readConcern: ReadConcern.Snapshot, writeConcern: WriteConcern.WMajority), ct);
        }
        catch (MongoWriteException e) when (e.WriteError.Category == ServerErrorCategory.DuplicateKey) { var existing = await ByKeyAsync(record.AccountId, record.IdempotencyKey, ct); if (existing != null) { return existing; } throw; }
    }
    private async Task<bool> HasAllocationConflictAsync(ReservationRecord record, IClientSessionHandle? session, CancellationToken ct, DateTime now)
    {
        var bookingFilter = Builders<ReservationRecord>.Filter;
        var overlapping = HoldingCapacity(now) & bookingFilter.Where(x => x.Id != record.Id &&
            x.StartUtc < record.EndUtc && x.EndUtc > record.StartUtc) & bookingFilter.AnyIn(x => x.ResourceIds, record.ResourceIds);
        var bookings = session is null ? Reservations.Find(overlapping) : Reservations.Find(session, overlapping);
        if (await bookings.AnyAsync(ct))
        {
            return true;
        }
        var blocks = context.Database.GetCollection<ScheduleBlock>("schedule_blocks");
        var blockFilter = Builders<ScheduleBlock>.Filter;
        var blocked = blockFilter.Where(x => x.ReleasedUtc == null && x.StartUtc < record.EndUtc && x.EndUtc > record.StartUtc) &
            (blockFilter.Size(x => x.ResourceIds, 0) | blockFilter.AnyIn(x => x.ResourceIds, record.ResourceIds));
        return await (session is null ? blocks.Find(blocked) : blocks.Find(session, blocked)).AnyAsync(ct);
    }
    public async Task ExpireAsync(DateTime now, string[] staffEmails, CancellationToken ct)
    {
        var filters = Builders<ReservationRecord>.Filter;
        var eligible = filters.Where(x => x.Status == "Pending" &&
            (x.Service != "WholeShop" && x.StartUtc <= now ||
             x.Service == "WholeShop" && x.PolicyVersion >= 1 && x.ReviewDeadlineUtc <= now));
        var due = await Reservations.Find(eligible).ToListAsync(ct);
        foreach (var record in due)
        {
            using var session = await context.Client.StartSessionAsync(cancellationToken: ct);
            await session.WithTransactionAsync(async (s, token) =>
            {
                await GuardAsync(s, record, token);
                var update = Builders<ReservationRecord>.Update.Inc(x => x.Revision, 1).Set(x => x.Status, "Expired")
                    .Set(x => x.CancellationRequested, false)
                    .Push(x => x.History, new HistoryRecord
                    {
                        EventType = "ReservationExpired",
                        ReservationStatus = "Expired",
                        Status = "Expired",
                        AtUtc = now,
                        Actor = "System"
                    });
                var result = await Reservations.UpdateOneAsync(s, eligible & filters.Eq(x => x.Id, record.Id), update, cancellationToken: token);
                if (result.ModifiedCount == 0) { return false; }
                foreach (var recipient in staffEmails.Append(record.Email).Where(email => !string.IsNullOrWhiteSpace(email)).Distinct())
                {
                    await context.Database.GetCollection<OutboxRecord>("outbox").InsertOneAsync(s, new OutboxRecord
                    {
                        Id = $"{record.Id}:expired:{recipient}",
                        Recipient = recipient,
                        Subject = "Reservation request expired",
                        Body = $"Request {record.Reference} expired before confirmation. Its bay hold has been released.",
                        DueUtc = now,
                        ReservationId = record.Id,
                        RequiredStatus = "Expired"
                    }, cancellationToken: token);
                }
                return true;
            }, cancellationToken: ct);
        }
    }
    public async Task AddResourceAsync(ResourceRecord resource, CancellationToken ct)
    {
        using var session = await context.Client.StartSessionAsync(cancellationToken: ct);
        await session.WithTransactionAsync(async (s, token) =>
        {
            await GuardCatalogAsync(s, token);
            var now = DateTime.UtcNow;
            if (await Reservations.Find(s, x => x.Service == "WholeShop" &&
                (x.Status == "Checked-In" || ((x.Status == "Pending" || x.Status == "Confirmed") && x.EndUtc > now))).AnyAsync(token))
            { throw new AllocationConflictException("Resource catalog changes require resolving active whole-shop allocations first."); }
            try { await Resources.InsertOneAsync(s, resource, cancellationToken: token); }
            catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
            { throw new AllocationConflictException("A bay with that ID already exists."); }
            return true;
        }, cancellationToken: ct);
    }

    public async Task<bool> SetResourceActiveAsync(string id, bool active, DateTime now, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        using var session = await context.Client.StartSessionAsync(cancellationToken: timeout.Token);
        return await session.WithTransactionAsync(async (s, token) =>
        {
            await GuardCatalogAsync(s, token);
            var resource = await Resources.Find(s, x => x.Id == id).FirstOrDefaultAsync(token);
            if (resource is null) { return false; }
            if (resource.Active == active) { return true; }
            var filters = Builders<ReservationRecord>.Filter;
            var affected = filters.Eq(x => x.Service, "WholeShop");
            if (!active) { affected |= filters.AnyEq(x => x.ResourceIds, id); }
            var blocking = filters.Eq(x => x.Status, "Checked-In") |
                (HoldingCapacity(now) & filters.Gt(x => x.EndUtc, now));
            var conflicts = await Reservations.Find(s, affected & blocking).AnyAsync(token);
            if (conflicts)
            {
                throw new AllocationConflictException("Resolve current or upcoming reservations on this bay and any whole-shop bookings before changing availability.");
            }
            await Resources.UpdateOneAsync(s, x => x.Id == id, Builders<ResourceRecord>.Update.Set(x => x.Active, active), cancellationToken: token);
            return true;
        }, cancellationToken: timeout.Token);
    }
    public async Task MarkOverdueAsync(DateTime now, string[] staffEmails, CancellationToken ct)
    {
        var filters = Builders<ReservationRecord>.Filter;
        const string marker = "Review overdue";
        var eligible = filters.Where(x => x.Status == "Pending" && x.Service != "WholeShop" &&
            x.ReviewDeadlineUtc <= now && x.StartUtc > now) & filters.Not(filters.ElemMatch(x => x.History, entry => entry.Status == marker));
        var due = await Reservations.Find(eligible).ToListAsync(ct);
        foreach (var record in due)
        {
            using var session = await context.Client.StartSessionAsync(cancellationToken: ct);
            await session.WithTransactionAsync(async (transaction, token) =>
            {
                await GuardAsync(transaction, record, token);
                var change = Builders<ReservationRecord>.Update.Inc(x => x.Revision, 1)
                    .Push(x => x.History, new HistoryRecord
                    {
                        ReservationStatus = "Pending",
                        Status = marker,
                        AtUtc = now,
                        Actor = "System",
                        Reason = "The review deadline passed and the bay hold was released."
                    });
                var applied = await Reservations.UpdateOneAsync(transaction, eligible & filters.Eq(x => x.Id, record.Id), change, cancellationToken: token);
                if (applied.ModifiedCount == 0) { return false; }

                var messages = staffEmails.Append(record.Email).Where(email => !string.IsNullOrWhiteSpace(email)).Distinct()
                    .Select(recipient => new OutboxRecord
                    {
                        Id = $"{record.Id}:overdue:{recipient}",
                        Recipient = recipient,
                        Subject = $"Overdue review: {record.Reference}",
                        Body = $"Request {record.Reference} is still awaiting staff review. Its bay hold has been released and confirmation depends on availability.",
                        DueUtc = now,
                        ReservationId = record.Id,
                        RequiredStatus = "Pending"
                    }).ToArray();
                if (messages.Length > 0)
                {
                    await context.Database.GetCollection<OutboxRecord>("outbox").InsertManyAsync(transaction, messages, cancellationToken: token);
                }
                return true;
            }, cancellationToken: ct);
        }
    }

    public async Task<IReadOnlyList<ReservationRecord>> PendingAsync(DateTime now, CancellationToken ct) =>
        await Reservations.Find(x => x.Status == "Pending" && x.ReviewDeadlineUtc > now && (x.Service != "WholeShop" || x.PolicyVersion >= 1)).ToListAsync(ct);
}
public sealed class AllocationConflictException(string message) : Exception(message);





