using HoleInHub.Data.Accounts;
using MongoDB.Bson;
using MongoDB.Driver;

namespace HoleInHub.Data.Migrations.Definitions;

public sealed class Migration_005_GoogleAccounts : IMongoMigration
{
    public string MigrationId => "005_GoogleAccounts";
    public string Description => "Optional account contact fields and unique Google identities";
    public string CalculateChecksum() => MongoMigrationChecksum.Compute("005_GoogleAccounts:v1:GoogleSubject.partialUniqueString");
    public Task UpAsync(IMongoDatabase database, CancellationToken ct) =>
        database.GetCollection<AccountRecord>("accounts").Indexes.CreateOneAsync(
            new CreateIndexModel<AccountRecord>(Builders<AccountRecord>.IndexKeys.Ascending(x => x.GoogleSubject),
                new CreateIndexOptions<AccountRecord>
                {
                    Name = "ux_accounts_google_subject",
                    Unique = true,
                    PartialFilterExpression = new BsonDocument("GoogleSubject", new BsonDocument("$type", "string"))
                }), cancellationToken: ct);
}
