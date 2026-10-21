using HoleInHub.Data.Entities;
using MongoDB.Driver;

namespace HoleInHub.Data.Context;

public interface IMongoDbContext
{
    IMongoClient Client { get; }
    IMongoDatabase Database { get; }
    IMongoCollection<MigrationRecord> Migrations { get; }
    IMongoCollection<MigrationLease> MigrationLeases { get; }
    IMongoCollection<SystemHeartbeat> SystemHeartbeats { get; }

    Task<bool> PingAsync(CancellationToken ct = default);
}
