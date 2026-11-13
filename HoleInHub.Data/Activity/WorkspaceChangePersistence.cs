using HoleInHub.Data.Context;
using MongoDB.Bson;
using MongoDB.Driver;

namespace HoleInHub.Data.Activity;

public sealed class WorkspaceChangePersistence(IMongoDbContext context)
{
    public async Task WatchAsync(Func<CancellationToken, Task> changed, CancellationToken ct)
    {
        var pipeline = new EmptyPipelineDefinition<ChangeStreamDocument<BsonDocument>>().Match(new BsonDocument
        {
            { "operationType", new BsonDocument("$in", new BsonArray { "insert", "update", "replace", "delete" }) },
            { "ns.coll", new BsonDocument("$in", new BsonArray { "reservations", "resources", "schedule_blocks", "customer_profiles",
                "customer_profile_operations", "staff_role_audit", "service_catalog", "system_settings" }) }
        });
        using var cursor = await context.Database.WatchAsync(pipeline, new ChangeStreamOptions { MaxAwaitTime = TimeSpan.FromSeconds(2) }, ct);
        while (await cursor.MoveNextAsync(ct))
        {
            if (cursor.Current.Any()) { await changed(ct); }
        }
    }
}
