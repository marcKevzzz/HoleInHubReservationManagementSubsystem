using HoleInHub.Data.Accounts;
using MongoDB.Driver;

namespace HoleInHub.Data.Migrations.Definitions;

public sealed class AccountsMigration : IMongoMigration
{
    public string MigrationId => "003_Accounts";
    public string Description => "Unique normalized account emails";
    public string CalculateChecksum() => MongoMigrationChecksum.Compute("003_Accounts:v1:accounts.normalizedEmail.unique");
    public Task UpAsync(IMongoDatabase database, CancellationToken ct) => database.GetCollection<AccountRecord>("accounts").Indexes.CreateOneAsync(new CreateIndexModel<AccountRecord>(Builders<AccountRecord>.IndexKeys.Ascending(x => x.NormalizedEmail), new CreateIndexOptions { Unique = true, Name = "ux_accounts_email" }), cancellationToken: ct);
}
