using HoleInHub.Data.Context;
using MongoDB.Driver;

namespace HoleInHub.Data.Accounts;

public sealed record AccountDirectoryRow(string Email, string FirstName, string LastName, string[] Roles, bool EmailConfirmed);

public sealed class AccountPersistence(IMongoDbContext context)
{
    private IMongoCollection<AccountRecord> Accounts => context.Database.GetCollection<AccountRecord>("accounts");
    public Task<AccountRecord?> FindByIdAsync(string id, CancellationToken ct) => Accounts.Find(x => x.Id == id).FirstOrDefaultAsync(ct)!;
    public Task<AccountRecord?> FindByEmailAsync(string email, CancellationToken ct) => Accounts.Find(x => x.NormalizedEmail == email).FirstOrDefaultAsync(ct)!;
    public Task<AccountRecord?> FindByGoogleSubjectAsync(string subject, CancellationToken ct) => Accounts.Find(x => x.GoogleSubject == subject).FirstOrDefaultAsync(ct)!;
    public Task<List<AccountDirectoryRow>> ReadDirectoryAsync(IReadOnlyList<string>? roles, int offset, int limit, CancellationToken ct)
    {
        var filter = roles is null ? Builders<AccountRecord>.Filter.Empty : Builders<AccountRecord>.Filter.AnyIn(x => x.Roles, roles);
        return Accounts.Find(filter).SortBy(x => x.NormalizedEmail).ThenBy(x => x.Id).Skip(offset).Limit(limit)
            .Project(x => new AccountDirectoryRow(x.Email, x.FirstName, x.LastName, x.Roles, x.EmailConfirmed)).ToListAsync(ct);
    }
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
