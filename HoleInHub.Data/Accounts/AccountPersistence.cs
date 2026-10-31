using HoleInHub.Data.Context;
using MongoDB.Driver;

namespace HoleInHub.Data.Accounts;

public sealed class AccountPersistence(IMongoDbContext context)
{
    private IMongoCollection<AccountRecord> Accounts => context.Database.GetCollection<AccountRecord>("accounts");
    public Task<AccountRecord?> FindByIdAsync(string id, CancellationToken ct) => Accounts.Find(x => x.Id == id).FirstOrDefaultAsync(ct)!;
    public Task<AccountRecord?> FindByEmailAsync(string email, CancellationToken ct) => Accounts.Find(x => x.NormalizedEmail == email).FirstOrDefaultAsync(ct)!;
    public async Task<bool> CreateAsync(AccountRecord account, CancellationToken ct)
    {
        try { await Accounts.InsertOneAsync(account, cancellationToken: ct); return true; }
        catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey) { return false; }
    }
    public async Task<bool> UpdateAsync(AccountRecord account, string expectedVersion, CancellationToken ct)
    {
        try
        {
            var result = await Accounts.ReplaceOneAsync(x => x.Id == account.Id && x.Version == expectedVersion, account, cancellationToken: ct);
            return result.ModifiedCount == 1;
        }
        catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey) { return false; }
    }
}
