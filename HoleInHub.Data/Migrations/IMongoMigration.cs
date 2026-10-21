using MongoDB.Driver;

namespace HoleInHub.Data.Migrations;

public interface IMongoMigration
{
    string MigrationId { get; }
    string Description { get; }
    string CalculateChecksum();
    Task UpAsync(IMongoDatabase database, CancellationToken ct);
}
