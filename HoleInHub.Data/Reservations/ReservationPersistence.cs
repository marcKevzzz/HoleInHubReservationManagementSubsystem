using HoleInHub.Data.Context;
using MongoDB.Bson;
using MongoDB.Driver;
namespace HoleInHub.Data.Reservations;

public sealed class ReservationPersistence(IMongoDbContext context)
{
    private IMongoCollection<ReservationRecord> Reservations => context.Database.GetCollection<ReservationRecord>("reservations");
    private IMongoCollection<ResourceRecord> Resources => context.Database.GetCollection<ResourceRecord>("resources");
    public async Task<IReadOnlyList<ResourceRecord>> CatalogAsync(CancellationToken ct) => await Resources.Find(FilterDefinition<ResourceRecord>.Empty).SortBy(x => x.Id).ToListAsync(ct);
    public async Task<IReadOnlyList<ReservationRecord>> OwnedAsync(string accountId, CancellationToken ct) => await Reservations.Find(x => x.AccountId == accountId).SortByDescending(x => x.CreatedUtc).ToListAsync(ct);
    public Task<ReservationRecord?> OwnedAsync(string accountId, string id, CancellationToken ct) => Reservations.Find(x => x.Id == id && x.AccountId == accountId).FirstOrDefaultAsync(ct)!;
    public Task<ReservationRecord?> ByKeyAsync(string accountId, string key, CancellationToken ct) => Reservations.Find(x => x.AccountId == accountId && x.IdempotencyKey == key).FirstOrDefaultAsync(ct)!;
    public async Task<string[]> OccupiedAsync(DateTime start, DateTime end, CancellationToken ct) { var records = await Reservations.Find(x => (x.Status == "Pending" || x.Status == "Confirmed" || x.Status == "Checked-In") && x.StartUtc < end && x.EndUtc > start).ToListAsync(ct); var blocks = await context.Database.GetCollection<ScheduleBlock>("schedule_blocks").Find(x => x.StartUtc < end && x.EndUtc > start).ToListAsync(ct); var catalog = await CatalogAsync(ct); return records.SelectMany(x => x.ResourceIds).Concat(blocks.SelectMany(x => x.ResourceIds.Length == 0 ? catalog.Select(r => r.Id) : x.ResourceIds)).Distinct().ToArray(); }
    private async Task GuardAsync(IClientSessionHandle session, ReservationRecord record, CancellationToken ct)
    {
        var guards = context.Database.GetCollection<BsonDocument>("allocation_guards"); var day = TimeZoneInfo.ConvertTimeFromUtc(record.StartUtc, TimeZoneInfo.FindSystemTimeZoneById("Asia/Manila")).ToString("yyyy-MM-dd"); foreach (var id in new[] { "catalog" }.Concat(record.ResourceIds.Order().Select(x => $"{x}:{day}")))
        {
            await guards.UpdateOneAsync(session, new BsonDocument("_id", id), new BsonDocument("$inc", new BsonDocument("version", 1)), new UpdateOptions { IsUpsert = true }, ct);
        }
    }
    public async Task<ReservationRecord> InsertAsync(ReservationRecord record, CancellationToken ct)
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

                var catalog = await Resources.Find(s, FilterDefinition<ResourceRecord>.Empty).ToListAsync(token);
                if (record.Service == "WholeShop")
                {
                    record.ResourceIds = catalog.Select(x => x.Id).Order().ToArray();
                    await GuardAsync(s, record, token);
                }

                if (record.ResourceIds.Length == 0 || record.ResourceIds.Any(id => !catalog.Any(r => r.Id == id && (record.Service == "WholeShop" || r.Service == record.Service))))
                {
                    throw new AllocationConflictException("One or more selected resources are unavailable for this service.");
                }

                var collision = await Reservations.Find(s, Builders<ReservationRecord>.Filter.Where(x => (x.Status == "Pending" || x.Status == "Confirmed" || x.Status == "Checked-In") && x.StartUtc < record.EndUtc && x.EndUtc > record.StartUtc) & Builders<ReservationRecord>.Filter.AnyIn(x => x.ResourceIds, record.ResourceIds)).AnyAsync(token);
                var blocked = await context.Database.GetCollection<ScheduleBlock>("schedule_blocks").Find(s, Builders<ScheduleBlock>.Filter.Where(x => x.StartUtc < record.EndUtc && x.EndUtc > record.StartUtc) & (Builders<ScheduleBlock>.Filter.Size(x => x.ResourceIds, 0) | Builders<ScheduleBlock>.Filter.AnyIn(x => x.ResourceIds, record.ResourceIds))).AnyAsync(token);
                if (collision || blocked)
                {
                    throw new AllocationConflictException("These bays are no longer available. Please choose another time or bay.");
                }

                await Reservations.InsertOneAsync(s, record, cancellationToken: token); return record;
            }, new TransactionOptions(readConcern: ReadConcern.Snapshot, writeConcern: WriteConcern.WMajority), ct);
        }
        catch (MongoWriteException e) when (e.WriteError.Category == ServerErrorCategory.DuplicateKey) { var existing = await ByKeyAsync(record.AccountId, record.IdempotencyKey, ct); if (existing != null) { return existing; } throw; }
    }
    public async Task ExpireAsync(DateTime now, string[] staffEmails, CancellationToken ct)
    {
        var due = await Reservations.Find(x => x.Status == "Pending" && x.ReviewDeadlineUtc <= now && x.Service != "WholeShop").ToListAsync(ct);
        foreach (var record in due) { using var session = await context.Client.StartSessionAsync(cancellationToken: ct); await session.WithTransactionAsync(async (s, token) => { await GuardAsync(s, record, token); var result = await Reservations.UpdateOneAsync(s, x => x.Id == record.Id && x.Status == "Pending" && x.ReviewDeadlineUtc <= now, Builders<ReservationRecord>.Update.Set(x => x.Status, "Expired").Push(x => x.History, new HistoryRecord { Status = "Expired", AtUtc = now, Actor = "System" }), cancellationToken: token); if (result.ModifiedCount == 1) { foreach (var recipient in staffEmails.Append(record.Email).Distinct()) { await context.Database.GetCollection<OutboxRecord>("outbox").InsertOneAsync(s, new OutboxRecord { Id = $"{record.Id}:expired:{recipient}", Recipient = recipient, Subject = "Reservation request expired", Body = $"Request {record.Reference} expired before staff approval. Its capacity hold has been released.", ReservationId = record.Id, RequiredStatus = "Expired", DueUtc = now }, cancellationToken: token); } } return true; }, cancellationToken: ct); }
    }
    public async Task AddResourceAsync(ResourceRecord resource, CancellationToken ct)
    {
        using var session = await context.Client.StartSessionAsync(cancellationToken: ct);
        await session.WithTransactionAsync(async (s, token) =>
        {
            await context.Database.GetCollection<BsonDocument>("allocation_guards").UpdateOneAsync(s, new BsonDocument("_id", "catalog"), new BsonDocument("$inc", new BsonDocument("version", 1)), new UpdateOptions { IsUpsert = true }, token);
            if (await Reservations.Find(s, x => x.Service == "WholeShop" && (x.Status == "Pending" || x.Status == "Confirmed") && x.EndUtc > DateTime.UtcNow).AnyAsync(token)) { throw new AllocationConflictException("Resource catalog changes require resolving active whole-shop allocations first."); }
            await Resources.InsertOneAsync(s, resource, cancellationToken: token); return true;
        }, cancellationToken: ct);
    }
    public async Task<IReadOnlyList<ReservationRecord>> PendingAsync(CancellationToken ct) => await Reservations.Find(x => x.Status == "Pending" && x.Service != "WholeShop").ToListAsync(ct);
}
public sealed class AllocationConflictException(string message) : Exception(message);





