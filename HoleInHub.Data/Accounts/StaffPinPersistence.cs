using HoleInHub.Data.Reservations;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace HoleInHub.Data.Accounts;

public sealed record StaffPinCredential([property: BsonId] string AccountId, string Hash, string Version);
public sealed record StaffPinAttempt([property: BsonId] string Id, int Count, DateTime ExpiresUtc);
public sealed record StaffPinEmailProof(string Id, string Email, DateTime NowUtc);

public sealed class StaffPinPersistence(HoleInHub.Data.Context.IMongoDbContext context)
{
    private IMongoCollection<StaffPinCredential> Credentials =>
        context.Database.GetCollection<StaffPinCredential>("staff_pin_credentials");

    public Task<StaffPinCredential?> ReadAsync(string accountId, CancellationToken ct) =>
        Credentials.Find(entry => entry.AccountId == accountId).FirstOrDefaultAsync(ct)!;

    public async Task<long?> ReadEnrollmentVersionAsync(CancellationToken ct)
    {
        var guard = await context.Database.GetCollection<BsonDocument>(AccountPersistence.RoleGuards)
            .Find(new BsonDocument("_id", AccountPersistence.RoleGuardId)).FirstOrDefaultAsync(ct);
        return guard is null ? null : guard["version"].ToInt64();
    }

    public async Task<List<StaffPinCredential>> ReadCandidatesAsync(IReadOnlyList<string> roles, CancellationToken ct)
    {
        var accounts = context.Database.GetCollection<AccountRecord>(AccountPersistence.AccountCollection);
        var eligible = Builders<AccountRecord>.Filter.Where(account => account.EmailConfirmed)
            & Builders<AccountRecord>.Filter.AnyIn(account => account.Roles, roles);
        var ids = await accounts.Find(eligible).Project(account => account.Id).ToListAsync(ct);
        return await Credentials.Find(Builders<StaffPinCredential>.Filter.In(credential => credential.AccountId, ids)).ToListAsync(ct);
    }

    public async Task<bool> TrySaveAsync(StaffPinCredential credential, string expectedStamp,
        IReadOnlyList<string> workspaceRoles, CancellationToken ct, StaffPinEmailProof? proof = null, long? expectedEnrollmentVersion = null)
    {
        var accounts = context.Database.GetCollection<AccountRecord>(AccountPersistence.AccountCollection);
        var filters = Builders<AccountRecord>.Filter;
        var eligible = filters.Where(x => x.Id == credential.AccountId && x.EmailConfirmed && x.SecurityStamp == expectedStamp)
            & filters.AnyIn(x => x.Roles, workspaceRoles);
        var update = Builders<AccountRecord>.Update.Set(x => x.Version, Guid.NewGuid().ToString("N"));

        using var session = await context.Client.StartSessionAsync(cancellationToken: ct);
        return await session.WithTransactionAsync(async (transaction, token) =>
        {
            if (proof is not null)
            {
                if (expectedEnrollmentVersion is null) { return false; }
                // A roster change after the cross-account PIN check requires another check before saving.
                var guard = await context.Database.GetCollection<BsonDocument>(AccountPersistence.RoleGuards).UpdateOneAsync(transaction,
                    new BsonDocument { { "_id", AccountPersistence.RoleGuardId }, { "version", expectedEnrollmentVersion.Value } },
                    new BsonDocument("$inc", new BsonDocument("version", 1L)), cancellationToken: token);
                if (guard.MatchedCount == 0) { return false; }
            }
            // Serialize enrollment with role and password changes before replacing the stored PIN.
            if ((await accounts.UpdateOneAsync(transaction, eligible, update, cancellationToken: token)).MatchedCount == 0)
            {
                await transaction.AbortTransactionAsync(token);
                return false;
            }

            if (proof is not null)
            {
                var challenges = context.Database.GetCollection<GuestVerificationRecord>("guest_verifications");
                var consumed = await challenges.UpdateOneAsync(transaction,
                    entry => entry.Id == proof.Id && entry.Email == proof.Email && entry.Purpose == "AccountEmail"
                        && entry.VerifiedUtc != null && entry.ExpiresUtc > proof.NowUtc && entry.ConsumedBySubmissionKey == null,
                    Builders<GuestVerificationRecord>.Update.Set(entry => entry.ConsumedBySubmissionKey, "StaffPinSetup"), cancellationToken: token);
                if (consumed.ModifiedCount != 1)
                {
                    await transaction.AbortTransactionAsync(token);
                    return false;
                }
            }

            await Credentials.ReplaceOneAsync(transaction, entry => entry.AccountId == credential.AccountId,
                credential, new ReplaceOptions { IsUpsert = true }, token);
            return true;
        }, cancellationToken: ct);
    }

    public async Task<bool> TakeAttemptAsync(string key, DateTime now, int limit, CancellationToken ct)
    {
        var window = TimeSpan.FromMinutes(15).Ticks;
        var start = now.Ticks - now.Ticks % window;
        var id = $"{key}:{start}";
        var attempts = context.Database.GetCollection<StaffPinAttempt>("staff_pin_attempts");
        var available = Builders<StaffPinAttempt>.Filter.Where(entry => entry.Id == id && entry.Count < limit);
        var increment = Builders<StaffPinAttempt>.Update.Inc(entry => entry.Count, 1)
            .SetOnInsert(entry => entry.ExpiresUtc, new DateTime(start + window, DateTimeKind.Utc));
        try
        {
            await attempts.UpdateOneAsync(available, increment, new UpdateOptions { IsUpsert = true }, ct);
            return true;
        }
        catch (MongoWriteException error) when (error.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            return (await attempts.UpdateOneAsync(available, increment, cancellationToken: ct)).ModifiedCount == 1;
        }
    }
}
