using MongoDB.Bson;
using MongoDB.Driver;

namespace HoleInHub.Data.Migrations.Definitions;

public sealed class Migration_022_PrefixedBusinessIdentifiers : IMongoMigration
{
    private static readonly (string Collection, string Field, string Prefix)[] Identifiers =
    [
        ("accounts", "account_id", "ACC"),
        ("customer_profiles", "customer_id", "CUS"),
        ("service_catalog", "service_id", "SVC"),
        ("schedule_blocks", "block_id", "BLK"),
        ("service_images", "service_image_id", "IMG"),
        ("system_settings", "settings_id", "SET")
    ];

    public string MigrationId => "022_PrefixedBusinessIdentifiers";
    public string Description => "Assign readable business codes to accounts, customers, services, blocks, images and settings";
    public string CalculateChecksum() => MongoMigrationChecksum.Compute(MigrationId + ":v1:sha256-utf8-first16-uppercase:" +
        string.Join(",", Identifiers.Select(id => $"{id.Collection}.{id.Field}={id.Prefix}")));

    public async Task UpAsync(IMongoDatabase database, CancellationToken ct)
    {
        using var metadata = await database.ListCollectionsAsync(cancellationToken: ct);
        var collections = (await metadata.ToListAsync(ct)).ToDictionary(item => item["name"].AsString);
        var validators = new Dictionary<string, BsonDocument>();
        foreach (var (name, field, prefix) in Identifiers)
        {
            var validator = collections[name]["options"]["validator"].AsBsonDocument;
            var property = validator["$jsonSchema"]["properties"][field].AsBsonDocument;
            var pattern = $"^{prefix}-[0-9A-F]{{16}}\\z";
            var equality = new BsonDocument("$expr", new BsonDocument("$eq", new BsonArray { "$" + field, "$_id" }));
            var clauses = validator.GetValue("$and", new BsonArray()).AsBsonArray;
            if (!clauses.Contains(equality) && property.GetValue("pattern", "") != pattern)
            {
                throw new InvalidOperationException($"Unexpected validator for '{name}.{field}'. Verify migration 021 before retrying 022.");
            }
            clauses.Remove(equality);
            if (clauses.Count == 0) { validator.Remove("$and"); }
            property["pattern"] = pattern;
            validators.Add(name, validator);
            var records = database.GetCollection<BsonDocument>(name);
            using var cursor = await records.Find(FilterDefinition<BsonDocument>.Empty)
                .Project(new BsonDocument { { "_id", 1 }, { field, 1 } }).ToCursorAsync(ct);
            var codes = new HashSet<string>(StringComparer.Ordinal);
            var occupied = new HashSet<string>(StringComparer.Ordinal);
            while (await cursor.MoveNextAsync(ct))
            {
                foreach (var row in cursor.Current)
                {
                    if (!row["_id"].IsString || string.IsNullOrWhiteSpace(row["_id"].AsString))
                    {
                        throw new InvalidOperationException($"Review invalid keys in '{name}' before retrying migration 022.");
                    }
                    var key = row["_id"].AsString;
                    var code = BusinessIdentifier.Format(prefix, key);
                    var stored = row.GetValue(field, BsonNull.Value);
                    if (stored != key && stored != code)
                    {
                        throw new InvalidOperationException($"Review unexpected values in '{name}.{field}' before retrying migration 022.");
                    }
                    if (!codes.Add(code))
                    {
                        throw new InvalidOperationException($"Duplicate generated code in '{name}.{field}'. Resolve the collision before retrying migration 022.");
                    }
                    if (stored == key && key != code && key.StartsWith(prefix + "-", StringComparison.Ordinal))
                    {
                        occupied.Add(key);
                    }
                }
            }
            if (codes.Overlaps(occupied))
            {
                throw new InvalidOperationException($"A generated '{name}.{field}' is occupied by another legacy key. Resolve the conflict before retrying migration 022.");
            }
        }

        foreach (var (name, field, prefix) in Identifiers)
        {
            await database.RunCommandAsync<BsonDocument>(new BsonDocument
            {
                { "collMod", name }, { "validator", validators[name] }, { "validationLevel", "strict" }, { "validationAction", "error" }
            }, cancellationToken: ct);
            var records = database.GetCollection<BsonDocument>(name);
            var legacy = new BsonDocument("$expr", new BsonDocument("$eq", new BsonArray { "$" + field, "$_id" }));
            using var cursor = await records.Find(legacy, new FindOptions { BatchSize = 500 }).Sort(new BsonDocument("_id", 1))
                .Project(new BsonDocument("_id", 1)).ToCursorAsync(ct);
            while (await cursor.MoveNextAsync(ct))
            {
                var updates = cursor.Current.Select(row =>
                {
                    var key = row["_id"].AsString;
                    return new UpdateOneModel<BsonDocument>(new BsonDocument { { "_id", key }, { field, key } },
                        new BsonDocument("$set", new BsonDocument(field, BusinessIdentifier.Format(prefix, key))));
                }).ToList();
                if (updates.Count > 0)
                {
                    var result = await records.BulkWriteAsync(updates, cancellationToken: ct);
                    if (result.MatchedCount != updates.Count)
                    {
                        throw new InvalidOperationException($"'{name}' changed during migration 022. Stop other writers and retry.");
                    }
                }
            }
        }
    }
}
