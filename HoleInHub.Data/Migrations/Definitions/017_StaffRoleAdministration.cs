
using HoleInHub.Data.Accounts;
using MongoDB.Bson;
using MongoDB.Driver;

namespace HoleInHub.Data.Migrations.Definitions;

public sealed class Migration_017_StaffRoleAdministration : IMongoMigration
{
    public string MigrationId => "017_StaffRoleAdministration";
    public string Description => "Create the staff role audit collection and seed the role administration guard";
    public string CalculateChecksum() => MongoMigrationChecksum.Compute(MigrationId + ":v1:staff_role_audit:staff_role_guards");

    public async Task UpAsync(IMongoDatabase database, CancellationToken ct)
    {
        using var collections = await database.ListCollectionNamesAsync(
            new ListCollectionNamesOptions { Filter = new BsonDocument("name", StaffRoleAuditRecord.CollectionName) }, ct);
        if (!await collections.AnyAsync(ct))
        {
            await database.CreateCollectionAsync(StaffRoleAuditRecord.CollectionName, cancellationToken: ct);
        }

        await database.GetCollection<BsonDocument>(AccountPersistence.RoleGuards).UpdateOneAsync(
            new BsonDocument("_id", AccountPersistence.RoleGuardId),
            new BsonDocument("$setOnInsert", new BsonDocument("version", 0L)),
            new UpdateOptions { IsUpsert = true }, ct);
    }
}
