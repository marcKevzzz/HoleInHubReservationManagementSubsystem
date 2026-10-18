using HoleInHub.Data.Configuration;
using HoleInHub.Data.Context;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Xunit;

namespace HoleInHub.Tests.Integration.Fixtures;


public sealed class MongoDbFixture : IAsyncLifetime
{
    public string ConnectionString { get; }
    public string DatabaseName { get; }
    public IMongoClient Client { get; private set; } = null!;
    public bool IsServerAvailable { get; private set; }

    public MongoDbFixture()
    {
        ConnectionString = Environment.GetEnvironmentVariable("MongoDb__ConnectionString")
                           ?? "mongodb://localhost:27017/?replicaSet=rs0";
        DatabaseName = $"test_holeinhub_{Guid.NewGuid():N}";
    }

    public async Task InitializeAsync()
    {
        try
        {
            var settings = MongoClientSettings.FromConnectionString(ConnectionString);
            settings.ServerSelectionTimeout = TimeSpan.FromSeconds(2);
            Client = new MongoClient(settings);

            var db = Client.GetDatabase(DatabaseName);
            await db.RunCommandAsync((Command<BsonDocument>)"{ping:1}");
            IsServerAvailable = true;
        }
        catch
        {
            IsServerAvailable = false;
        }
    }

    public IMongoDbContext CreateDbContext()
    {
        var options = Options.Create(new MongoDbOptions
        {
            ConnectionString = ConnectionString,
            DatabaseName = DatabaseName,
            ServerSelectionTimeoutMs = 2000,
            SocketTimeoutMs = 5000
        });

        return new MongoDbContext(options);
    }

    public async Task DisposeAsync()
    {
        if (IsServerAvailable && Client != null)
        {
            try
            {
                await Client.DropDatabaseAsync(DatabaseName);
            }
            catch
            {
                // Best-effort test cleanup
            }
        }
    }
}
