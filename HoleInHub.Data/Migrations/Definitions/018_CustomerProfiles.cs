using HoleInHub.Data.Context;
using HoleInHub.Data.Customers;
using MongoDB.Bson;
using MongoDB.Driver;

namespace HoleInHub.Data.Migrations.Definitions;

public sealed class Migration_018_CustomerProfiles : IMongoMigration
{
    public string MigrationId => "018_CustomerProfiles";
    public string Description => "Add staff-only customer profiles, booking memberships and audited consolidation";
    public string CalculateChecksum() => MongoMigrationChecksum.Compute(MigrationId + ":v1:profiles:memberships:operations:guards:preserve-ownership");
    public async Task UpAsync(IMongoDatabase database, CancellationToken ct)
    {
        var schemas = new Dictionary<string, string>
        {
            [CustomerProfilePersistence.ProfilesName] = "{bsonType:'object',required:['_id','FirstName','LastName','Email','Phone','RepresentativeBookingId','ContactRevision','MembershipRevision'],properties:{_id:{bsonType:'string'},FirstName:{bsonType:'string'},LastName:{bsonType:'string'},Email:{bsonType:'string'},Phone:{bsonType:'string'},Variants:{bsonType:'array'},Sources:{bsonType:'array'},SearchNames:{bsonType:'array'},MergedIntoId:{bsonType:['string','null']},ContactRevision:{bsonType:'long'},MembershipRevision:{bsonType:'long'},CreatedUtc:{bsonType:'date'},UpdatedUtc:{bsonType:'date'}}}",
            [CustomerProfilePersistence.MembershipsName] = "{bsonType:'object',required:['_id','ProfileId','Revision'],properties:{_id:{bsonType:'string'},ProfileId:{bsonType:'string'},Revision:{bsonType:'long'},LastOperationId:{bsonType:['string','null']}}}",
            [CustomerProfilePersistence.OperationsName] = "{bsonType:'object',required:['_id','Kind','SourceId','TargetId','ActorId','Reason','OccurredUtc','ReservationIds'],properties:{_id:{bsonType:'string'},Kind:{enum:['Merge','Undo']},SourceId:{bsonType:'string'},TargetId:{bsonType:'string'},ActorId:{bsonType:'string'},Reason:{bsonType:'string',minLength:1,maxLength:500},OccurredUtc:{bsonType:'date'},ReservationIds:{bsonType:'array',minItems:1,maxItems:1000},SourceBefore:{bsonType:'object'},TargetBefore:{bsonType:'object'},UndoneById:{bsonType:['string','null']},ReversesId:{bsonType:['string','null']}}}",
            [CustomerProfilePersistence.GuardsName] = "{bsonType:'object',required:['_id','version'],properties:{_id:{bsonType:'string'},version:{bsonType:'long'}}}"
        };
        using var cursor = await database.ListCollectionNamesAsync(cancellationToken: ct);
        var existing = await cursor.ToListAsync(ct);
        foreach (var (name, schema) in schemas)
        {
            var validator = new BsonDocument("$jsonSchema", BsonDocument.Parse(schema));
            if (!existing.Contains(name))
            {
                await database.CreateCollectionAsync(name, new CreateCollectionOptions<BsonDocument> { Validator = validator }, ct);
            }
            else
            {
                await database.RunCommandAsync<BsonDocument>(new BsonDocument { { "collMod", name }, { "validator", validator }, { "validationLevel", "strict" } }, cancellationToken: ct);
            }
        }
        var profiles = database.GetCollection<CustomerProfileRecord>(CustomerProfilePersistence.ProfilesName);
        await profiles.Indexes.CreateOneAsync(new CreateIndexModel<CustomerProfileRecord>(Builders<CustomerProfileRecord>.IndexKeys.Ascending(x => x.MergedIntoId).Descending(x => x.UpdatedUtc).Ascending(x => x.Id)), cancellationToken: ct);
        await database.GetCollection<CustomerMembershipRecord>(CustomerProfilePersistence.MembershipsName).Indexes.CreateOneAsync(
            new CreateIndexModel<CustomerMembershipRecord>(Builders<CustomerMembershipRecord>.IndexKeys.Ascending(x => x.ProfileId).Ascending(x => x.Id)), cancellationToken: ct);
        var audit = database.GetCollection<CustomerProfileOperationRecord>(CustomerProfilePersistence.OperationsName);
        await audit.Indexes.CreateManyAsync([
            new(Builders<CustomerProfileOperationRecord>.IndexKeys.Ascending(x => x.SourceId).Descending(x => x.OccurredUtc).Descending(x => x.Id)),
            new(Builders<CustomerProfileOperationRecord>.IndexKeys.Ascending(x => x.TargetId).Descending(x => x.OccurredUtc).Descending(x => x.Id))
        ], ct);
        var guards = database.GetCollection<BsonDocument>(CustomerProfilePersistence.GuardsName);
        await guards.UpdateOneAsync(new BsonDocument("_id", "memberships"), new BsonDocument("$setOnInsert", new BsonDocument("version", 0L)), new UpdateOptions { IsUpsert = true }, ct);
        var context = new MigrationContext(database);
        await new CustomerProfilePersistence(context).BackfillAsync(ct);
        await guards.UpdateOneAsync(new BsonDocument("_id", "installed"), new BsonDocument("$setOnInsert", new BsonDocument("version", 1L)), new UpdateOptions { IsUpsert = true }, ct);
    }
    private sealed class MigrationContext(IMongoDatabase database) : IMongoDbContext
    {
        public IMongoClient Client => database.Client;
        public IMongoDatabase Database => database;
        public IMongoCollection<HoleInHub.Data.Entities.MigrationRecord> Migrations => database.GetCollection<HoleInHub.Data.Entities.MigrationRecord>("__migrations");
        public IMongoCollection<HoleInHub.Data.Entities.MigrationLease> MigrationLeases => database.GetCollection<HoleInHub.Data.Entities.MigrationLease>("__migration_leases");
        public IMongoCollection<HoleInHub.Data.Entities.SystemHeartbeat> SystemHeartbeats => database.GetCollection<HoleInHub.Data.Entities.SystemHeartbeat>("system_heartbeats");
        public async Task<bool> PingAsync(CancellationToken ct = default) { await database.RunCommandAsync<BsonDocument>(new BsonDocument("ping", 1), cancellationToken: ct); return true; }
    }
}
