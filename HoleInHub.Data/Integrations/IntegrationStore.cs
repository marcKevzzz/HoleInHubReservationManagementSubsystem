using HoleInHub.Data.Context;
using MongoDB.Driver;

namespace HoleInHub.Data.Integrations;

public sealed record IntegrationKey(string Id, string Name, string Digest, string[] Permissions, DateTime ExpiresUtc, bool Revoked)
{
    public string Subsystem { get; init; } = "POS";
    public bool Deleted { get; init; }
}
public sealed record IntegrationRequest(string Id, string? KeyId, string Route, int Status, DateTime AtUtc, long DurationMs);

public sealed class IntegrationStore(IMongoDbContext context)
{
    private IMongoCollection<IntegrationKey> Keys => context.Database.GetCollection<IntegrationKey>("integration_keys");
    private IMongoCollection<IntegrationRequest> Requests => context.Database.GetCollection<IntegrationRequest>("integration_requests");
    public async Task<bool> TryAddAsync(IntegrationKey key, CancellationToken ct)
    {
        try { await Keys.InsertOneAsync(key, cancellationToken: ct); return true; }
        catch (MongoWriteException error) when (error.WriteError.Category == ServerErrorCategory.DuplicateKey) { return false; }
    }
    // Missing Deleted on older keys means the key is still listed.
    private static readonly FilterDefinition<IntegrationKey> Visible = Builders<IntegrationKey>.Filter.Ne(k => k.Deleted, true);
    public Task<IntegrationKey> FindAsync(string id, CancellationToken ct) => Keys.Find(Visible & Builders<IntegrationKey>.Filter.Eq(k => k.Id, id)).FirstOrDefaultAsync(ct);
    public Task<List<IntegrationKey>> ListAsync(CancellationToken ct) => Keys.Find(Visible).SortBy(k => k.Name).Limit(200).ToListAsync(ct);
    public Task DeleteAsync(string id, CancellationToken ct) => Keys.UpdateOneAsync(k => k.Id == id,
        Builders<IntegrationKey>.Update.Set(k => k.Deleted, true).Set(k => k.Digest, ""), cancellationToken: ct);
    public Task LogAsync(IntegrationRequest entry, CancellationToken ct) => Requests.InsertOneAsync(entry, cancellationToken: ct);
    public Task<List<IntegrationRequest>> LogsAsync(string? key, CancellationToken ct) => Requests.Find(r => key == null || r.KeyId == key).SortByDescending(r => r.AtUtc).Limit(100).ToListAsync(ct);
}

