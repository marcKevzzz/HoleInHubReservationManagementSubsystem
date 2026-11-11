using MongoDB.Driver;

namespace HoleInHub.Data.Reservations;

public sealed partial class ReservationPersistence
{
    private IMongoCollection<ScheduleBlock> Blocks => context.Database.GetCollection<ScheduleBlock>("schedule_blocks");

    public Task<ScheduleBlock?> BlockByIdAsync(string id, CancellationToken ct) =>
        Blocks.Find(x => x.Id == id).FirstOrDefaultAsync(ct)!;

    public async Task<bool> ReleaseBlockAsync(string id, string actor, string reason, DateTime now, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        ct = timeout.Token;
        using var session = await context.Client.StartSessionAsync(cancellationToken: ct);
        return await session.WithTransactionAsync(async (s, token) =>
        {
            var block = await Blocks.Find(s, x => x.Id == id).FirstOrDefaultAsync(token);
            if (block is null) { return false; }
            await GuardAsync(s, new ReservationRecord
            { ResourceIds = block.ResourceIds, StartUtc = block.StartUtc, EndUtc = block.EndUtc }, token);
            if (block.ReleasedUtc is not null) { return true; }
            await Blocks.UpdateOneAsync(s, x => x.Id == id && x.ReleasedUtc == null,
                Builders<ScheduleBlock>.Update.Set(x => x.ReleasedUtc, now).Set(x => x.ReleasedBy, actor)
                    .Set(x => x.ReleaseReason, reason), cancellationToken: token);
            return true;
        }, new TransactionOptions(readConcern: ReadConcern.Snapshot, writeConcern: WriteConcern.WMajority), ct);
    }

    public async Task<ScheduleBlock> InsertBlockAsync(ScheduleBlock block, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        ct = timeout.Token;
        using var session = await context.Client.StartSessionAsync(cancellationToken: ct);
        try
        {
            return await session.WithTransactionAsync(async (s, token) =>
            {
                var allocation = new ReservationRecord { ResourceIds = block.ResourceIds, StartUtc = block.StartUtc, EndUtc = block.EndUtc };
                await GuardAsync(s, allocation, token);
                var prior = await Blocks.Find(s, x => x.Id == block.Id).FirstOrDefaultAsync(token);
                if (prior is not null) { return prior; }
                if (block.ResourceIds.Length != 1 || !await Resources.Find(s, x => x.Id == block.ResourceIds[0]
                    && x.Active && (x.Service == "Regular" || x.Service == "VIP")).AnyAsync(token))
                {
                    throw new AllocationConflictException("Choose an active Regular or VIP bay.");
                }
                if (await HasAllocationConflictAsync(allocation, s, token, block.CreatedUtc ?? DateTime.UtcNow))
                {
                    throw new AllocationConflictException("This bay is no longer free for that time. Choose another bay or time.");
                }
                await Blocks.InsertOneAsync(s, block, cancellationToken: token);
                return block;
            }, new TransactionOptions(readConcern: ReadConcern.Snapshot, writeConcern: WriteConcern.WMajority), ct);
        }
        catch (MongoWriteException error) when (error.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            var prior = await BlockByIdAsync(block.Id, ct);
            if (prior is not null) { return prior; }
            throw;
        }
    }
}
