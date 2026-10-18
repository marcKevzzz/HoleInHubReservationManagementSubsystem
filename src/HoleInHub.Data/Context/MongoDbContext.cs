using HoleInHub.Data.Configuration;
using HoleInHub.Data.Entities;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;

namespace HoleInHub.Data.Context;

public sealed class MongoDbContext : IMongoDbContext
{
    public const string MigrationsCollectionName = "__migrations";
    public const string MigrationLeasesCollectionName = "__migration_leases";
    public const string HeartbeatsCollectionName = "system_heartbeats";

    public IMongoClient Client { get; }
    public IMongoDatabase Database { get; }

    public IMongoCollection<MigrationRecord> Migrations =>
        Database.GetCollection<MigrationRecord>(MigrationsCollectionName);

    public IMongoCollection<MigrationLease> MigrationLeases =>
        Database.GetCollection<MigrationLease>(MigrationLeasesCollectionName);

    public IMongoCollection<SystemHeartbeat> SystemHeartbeats =>
        Database.GetCollection<SystemHeartbeat>(HeartbeatsCollectionName);

    public MongoDbContext(IOptions<MongoDbOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var config = options.Value;
        config.Validate();

        var settings = MongoClientSettings.FromConnectionString(config.ConnectionString);
        settings.ServerSelectionTimeout = TimeSpan.FromMilliseconds(config.ServerSelectionTimeoutMs);
        settings.SocketTimeout = TimeSpan.FromMilliseconds(config.SocketTimeoutMs);

        if (!string.IsNullOrWhiteSpace(config.ReplicaSetName))
        {
            settings.ReplicaSetName = config.ReplicaSetName;
        }

        Client = new MongoClient(settings);
        Database = Client.GetDatabase(config.DatabaseName);
    }

    public async Task<bool> PingAsync(CancellationToken ct = default)
    {
        try
        {
            var pingCommand = new BsonDocument("ping", 1);
            await Database.RunCommandAsync<BsonDocument>(pingCommand, cancellationToken: ct);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
