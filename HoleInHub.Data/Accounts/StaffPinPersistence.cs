using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace HoleInHub.Data.Accounts;

public sealed record StaffPinCredential([property: BsonId] string AccountId, string Hash, string Version);
public sealed record StaffPinAttempt([property: BsonId] string Id, int Count, DateTime ExpiresUtc);

public sealed class StaffPinPersistence(HoleInHub.Data.Context.IMongoDbContext context)
{
    private IMongoCollection<StaffPinCredential> Credentials =>
        context.Database.GetCollection<StaffPinCredential>("staff_pin_credentials");

    public Task<StaffPinCredential?> ReadAsync(string accountId, CancellationToken ct) =>
        Credentials.Find(entry => entry.AccountId == accountId).FirstOrDefaultAsync(ct)!;

    public async Task<bool> TrySaveAsync(StaffPinCredential credential, string expectedStamp,
        IReadOnlyList<string> workspaceRoles, CancellationToken ct)
    {
        var accounts = context.Database.GetCollection<AccountRecord>(AccountPersistence.AccountCollection);
        var filters = Builders<AccountRecord>.Filter;
        var eligible = filters.Where(x => x.Id == credential.AccountId && x.EmailConfirmed && x.SecurityStamp == expectedStamp)
            & filters.AnyIn(x => x.Roles, workspaceRoles);
        var update = Builders<AccountRecord>.Update.Set(x => x.Version, Guid.NewGuid().ToString("N"));

        using var session = await context.Client.StartSessionAsync(cancellationToken: ct);
        return await session.WithTransactionAsync(async (transaction, token) =>
        {
            // Serialize enrollment with role and password changes before replacing the stored PIN.
            if ((await accounts.UpdateOneAsync(transaction, eligible, update, cancellationToken: token)).MatchedCount == 0)
            {
                return false;
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
