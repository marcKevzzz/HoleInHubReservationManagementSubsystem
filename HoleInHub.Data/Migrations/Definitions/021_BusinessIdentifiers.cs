using MongoDB.Bson;
using MongoDB.Driver;

namespace HoleInHub.Data.Migrations.Definitions;

public sealed class Migration_021_BusinessIdentifiers : IMongoMigration
{
    private static readonly (string Collection, string Field, string Source)[] Identifiers =
    [
        ("reservations", "reservation_id", "Reference"),
        ("customer_profiles", "customer_id", "_id"),
        ("accounts", "account_id", "_id"),
        ("resources", "resource_id", "_id"),
        ("service_catalog", "service_id", "_id"),
        ("schedule_blocks", "block_id", "_id"),
        ("service_images", "service_image_id", "_id"),
        ("system_settings", "settings_id", "_id")
    ];

    public string MigrationId => "021_BusinessIdentifiers";
    public string Description => "Persist and enforce named business identifiers without changing existing keys";
    public string CalculateChecksum() => MongoMigrationChecksum.Compute(MigrationId + ":v1:reservations-start-id:required-nonblank:source-equality:unique:" +
        string.Join(",", Identifiers.Select(id => $"{id.Collection}.{id.Field}={id.Source}")));

    public async Task UpAsync(IMongoDatabase database, CancellationToken ct)
    {
        using var cursor = await database.ListCollectionsAsync(cancellationToken: ct);
        var collections = (await cursor.ToListAsync(ct)).ToDictionary(item => item["name"].AsString);
        foreach (var (name, field, source) in Identifiers)
        {
            if (!collections.ContainsKey(name) && name != "service_images")
            {
                throw new InvalidOperationException($"Expected collection '{name}' before migration 021.");
            }

            var records = database.GetCollection<BsonDocument>(name);
            var sourceSchema = new BsonDocument("$jsonSchema", new BsonDocument
            {
                { "required", new BsonArray { source } }, { "properties", new BsonDocument(source, NonblankString()) }
            });
            var conflictingId = new BsonDocument
            {
                { field, new BsonDocument("$exists", true) },
                { "$expr", new BsonDocument("$ne", new BsonArray { "$" + field, "$" + source }) }
            };
            var invalid = new BsonDocument("$or", new BsonArray { new BsonDocument("$nor", new BsonArray { sourceSchema }), conflictingId });
            if (await records.Find(invalid).AnyAsync(ct))
            {
                throw new InvalidOperationException($"Review '{name}': '{source}' must be a nonblank string and any existing '{field}' must match it. Retry migration 021 after repair.");
            }

            // MongoDB already enforces uniqueness for every _id.
            if (source != "_id" && await records.Aggregate()
                .Group(new BsonDocument { { "_id", "$" + source }, { "count", new BsonDocument("$sum", 1) } })
                .Match(new BsonDocument("count", new BsonDocument("$gt", 1))).Limit(1).AnyAsync(ct))
            {
                throw new InvalidOperationException($"Review duplicate '{name}.{source}' values before retrying migration 021.");
            }
        }

        foreach (var (name, field, source) in Identifiers)
        {
            var exists = collections.TryGetValue(name, out var metadata);
            var validator = exists
                ? metadata!["options"].AsBsonDocument.GetValue("validator", new BsonDocument()).AsBsonDocument
                : new BsonDocument();
            var schema = validator.GetValue("$jsonSchema", new BsonDocument("bsonType", "object")).AsBsonDocument;
            var required = schema.GetValue("required", new BsonArray()).AsBsonArray;
            if (!required.Contains(field)) { required.Add(field); }
            schema["required"] = required;
            var properties = schema.GetValue("properties", new BsonDocument()).AsBsonDocument;
            properties[field] = NonblankString();
            schema["properties"] = properties;
            validator["$jsonSchema"] = schema;
            var clauses = validator.GetValue("$and", new BsonArray()).AsBsonArray;
            var equality = new BsonDocument("$expr", new BsonDocument("$eq", new BsonArray { "$" + field, "$" + source }));
            if (!clauses.Contains(equality)) { clauses.Add(equality); }
            validator["$and"] = clauses;

            if (exists)
            {
                await database.RunCommandAsync<BsonDocument>(new BsonDocument
                {
                    { "collMod", name }, { "validator", validator }, { "validationLevel", "strict" }, { "validationAction", "error" }
                }, cancellationToken: ct);
            }
            else
            {
                await database.CreateCollectionAsync(name, new CreateCollectionOptions<BsonDocument> { Validator = validator }, ct);
            }
            var records = database.GetCollection<BsonDocument>(name);
            await records.UpdateManyAsync(new BsonDocument(field, new BsonDocument("$exists", false)),
                new PipelineUpdateDefinition<BsonDocument>(new[] { new BsonDocument("$set", new BsonDocument(field, "$" + source)) }),
                cancellationToken: ct);
            await records.Indexes.CreateOneAsync(new CreateIndexModel<BsonDocument>(Builders<BsonDocument>.IndexKeys.Ascending(field),
                new CreateIndexOptions { Name = $"ux_{name}_{field}", Unique = true }), cancellationToken: ct);
        }
        await database.GetCollection<BsonDocument>("reservations").Indexes.CreateOneAsync(
            new CreateIndexModel<BsonDocument>(Builders<BsonDocument>.IndexKeys.Ascending("StartUtc").Ascending("_id"),
                new CreateIndexOptions { Name = "ix_reservations_start_id" }), cancellationToken: ct);
    }

    private static BsonDocument NonblankString() => new() { { "bsonType", "string" }, { "pattern", @"\S" } };
}
