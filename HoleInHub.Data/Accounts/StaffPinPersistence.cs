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

    public Task SaveAsync(StaffPinCredential credential, CancellationToken ct) =>
        Credentials.ReplaceOneAsync(entry => entry.AccountId == credential.AccountId, credential, new ReplaceOptions { IsUpsert = true }, ct);

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
