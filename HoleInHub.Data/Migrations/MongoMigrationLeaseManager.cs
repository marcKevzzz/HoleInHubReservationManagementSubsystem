using HoleInHub.Data.Context;
using HoleInHub.Data.Entities;
using MongoDB.Driver;

namespace HoleInHub.Data.Migrations;

public sealed class MigrationLeaseException(string message) : Exception(message);
public sealed class MigrationLeaseLostException(string message, Exception? innerException = null) : Exception(message, innerException);

public sealed class MongoMigrationLeaseManager(IMongoDbContext dbContext)
{
    private readonly IMongoDbContext _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));

    public async Task<bool> TryAcquireLeaseAsync(
        string runnerId,
        TimeSpan leaseDuration,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runnerId);

        var now = DateTime.UtcNow;
        var expiresAt = now.Add(leaseDuration);

        var filter = Builders<MigrationLease>.Filter.And(
            Builders<MigrationLease>.Filter.Eq(x => x.LeaseId, MigrationLease.DefaultLeaseId),
            Builders<MigrationLease>.Filter.Or(
                Builders<MigrationLease>.Filter.Eq(x => x.Owner, runnerId),
                Builders<MigrationLease>.Filter.Lt(x => x.ExpiresAtUtc, now)
            )
        );

        var update = Builders<MigrationLease>.Update
            .Set(x => x.Owner, runnerId)
            .Set(x => x.AcquiredAtUtc, now)
            .Set(x => x.ExpiresAtUtc, expiresAt)
            .Set(x => x.LastHeartbeatAtUtc, now)
            .SetOnInsert(x => x.LeaseId, MigrationLease.DefaultLeaseId);

        var options = new FindOneAndUpdateOptions<MigrationLease>
        {
            IsUpsert = true,
            ReturnDocument = ReturnDocument.After
        };

        try
        {
            var result = await _dbContext.MigrationLeases.FindOneAndUpdateAsync(filter, update, options, ct);
            return result != null && result.Owner == runnerId;
        }
        catch (MongoCommandException ex) when (ex.Code == 11000)
        {
            return false;
        }
    }

    public async Task RenewLeaseAsync(
        string runnerId,
        TimeSpan extension,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runnerId);

        var now = DateTime.UtcNow;
        var newExpiresAt = now.Add(extension);

        var filter = Builders<MigrationLease>.Filter.And(
            Builders<MigrationLease>.Filter.Eq(x => x.LeaseId, MigrationLease.DefaultLeaseId),
            Builders<MigrationLease>.Filter.Eq(x => x.Owner, runnerId),
            Builders<MigrationLease>.Filter.Gte(x => x.ExpiresAtUtc, now)
        );

        var update = Builders<MigrationLease>.Update
            .Set(x => x.ExpiresAtUtc, newExpiresAt)
            .Set(x => x.LastHeartbeatAtUtc, now);

        var result = await _dbContext.MigrationLeases.FindOneAndUpdateAsync(filter, update, cancellationToken: ct);
        if (result == null)
        {
            throw new MigrationLeaseLostException(
                $"Migration lease for runner '{runnerId}' was lost or expired and cannot be renewed.");
        }
    }

    public async Task ReleaseLeaseAsync(string runnerId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runnerId);

        var filter = Builders<MigrationLease>.Filter.And(
            Builders<MigrationLease>.Filter.Eq(x => x.LeaseId, MigrationLease.DefaultLeaseId),
            Builders<MigrationLease>.Filter.Eq(x => x.Owner, runnerId)
        );

        var update = Builders<MigrationLease>.Update
            .Set(x => x.ExpiresAtUtc, DateTime.UtcNow.AddSeconds(-1));

        await _dbContext.MigrationLeases.UpdateOneAsync(filter, update, cancellationToken: ct);
    }
}
