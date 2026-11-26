using MongoDB.Bson;
using MongoDB.Driver;

namespace HoleInHub.Data.Migrations.Definitions;

public sealed class Migration_031_CoachServiceAssignments : IMongoMigration
{
    public string MigrationId => "031_CoachServiceAssignments";
    public string Description => "Index coach commitments across individual bookings and published classes";
    public string CalculateChecksum() => MongoMigrationChecksum.Compute(MigrationId + ":v1:coach-range:class-coach-range");

    public async Task UpAsync(IMongoDatabase database, CancellationToken ct)
    {
        var keys = Builders<BsonDocument>.IndexKeys;
        await database.GetCollection<BsonDocument>("reservations").Indexes.CreateOneAsync(
            new CreateIndexModel<BsonDocument>(keys.Ascending("coach_id").Ascending("Status").Ascending("StartUtc").Ascending("EndUtc"),
                new CreateIndexOptions { Name = "ix_named_coach_commitments" }), cancellationToken: ct);
        await database.GetCollection<BsonDocument>("class_sessions").Indexes.CreateOneAsync(
            new CreateIndexModel<BsonDocument>(keys.Ascending("CoachId").Ascending("State").Ascending("StartUtc").Ascending("EndUtc"),
                new CreateIndexOptions { Name = "ix_class_coach_commitments" }), cancellationToken: ct);
    }
}
