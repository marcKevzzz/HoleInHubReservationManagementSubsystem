using HoleInHub.Data.Context;
using MongoDB.Driver;

namespace HoleInHub.Data.Integrations;

public sealed record IntegrationKey(string Id, string Name, string Digest, string[] Permissions, DateTime ExpiresUtc, bool Revoked)
{
    public string Subsystem { get; init; } = "POS";
}
public sealed record IntegrationRequest(string Id, string? KeyId, string Route, int Status, DateTime AtUtc, long DurationMs);

public sealed class IntegrationStore(IMongoDbContext context)
{
    private IMongoCollection<IntegrationKey> Keys => context.Database.GetCollection<IntegrationKey>("integration_keys");
    private IMongoCollection<IntegrationRequest> Requests => context.Database.GetCollection<IntegrationRequest>("integration_requests");
    public Task AddAsync(IntegrationKey key, CancellationToken ct) => Keys.InsertOneAsync(key, cancellationToken: ct);
    public Task<IntegrationKey> FindAsync(string id, CancellationToken ct) => Keys.Find(k => k.Id == id).FirstOrDefaultAsync(ct);
    public Task<List<IntegrationKey>> ListAsync(CancellationToken ct) => Keys.Find(_ => true).SortBy(k => k.Name).Limit(200).ToListAsync(ct);
    public Task RevokeAsync(string id, CancellationToken ct) => Keys.UpdateOneAsync(k => k.Id == id, Builders<IntegrationKey>.Update.Set(k => k.Revoked, true), cancellationToken: ct);
    public Task LogAsync(IntegrationRequest entry, CancellationToken ct) => Requests.InsertOneAsync(entry, cancellationToken: ct);
    public Task<List<IntegrationRequest>> LogsAsync(string? key, CancellationToken ct) => Requests.Find(r => key == null || r.KeyId == key).SortByDescending(r => r.AtUtc).Limit(100).ToListAsync(ct);
}

