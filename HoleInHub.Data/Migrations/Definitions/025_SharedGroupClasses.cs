using MongoDB.Bson;
using MongoDB.Driver;

namespace HoleInHub.Data.Migrations.Definitions;

public sealed class Migration_025_SharedGroupClasses : IMongoMigration
{
    public string MigrationId => "025_SharedGroupClasses";
    public string Description => "Add published shared KPGA classes, bounded seat counts and optional family rosters";
    private static readonly BsonDocument ClassSchema = BsonDocument.Parse("""{"bsonType":"object","required":["_id","class_session_id","ServiceEntryId","StartUtc","EndUtc","ResourceIds","Capacity","HeldSeats","State","PricePerLearner","CreatedBy"],"properties":{"_id":{"bsonType":"string","minLength":1,"maxLength":128},"class_session_id":{"bsonType":"string","pattern":"^CLS-[0-9A-F]{16}\\z"},"ServiceEntryId":{"bsonType":"string","minLength":1,"maxLength":128},"StartUtc":{"bsonType":"date"},"EndUtc":{"bsonType":"date"},"ResourceIds":{"bsonType":"array","minItems":1,"maxItems":8,"items":{"bsonType":"string","minLength":1,"maxLength":128},"uniqueItems":true},"Capacity":{"bsonType":"int","minimum":1,"maximum":8},"HeldSeats":{"bsonType":"int","minimum":0,"maximum":8},"State":{"enum":["Open","Closed","Released"]},"PricePerLearner":{"bsonType":"decimal","minimum":0},"CreatedBy":{"bsonType":"string","minLength":1,"maxLength":128}}}""");
    private static readonly BsonDocument ReservationFields = BsonDocument.Parse("""{"class_session_id":{"bsonType":"string","pattern":"^CLS-[0-9A-F]{16}\\z"},"Learners":{"bsonType":"array","minItems":1,"maxItems":8,"items":{"bsonType":"object","required":["_id","Name","AgeCategory","State","Attendance"],"properties":{"_id":{"bsonType":"string","minLength":1,"maxLength":128},"Name":{"bsonType":"string","minLength":1,"maxLength":120},"AgeCategory":{"enum":["Adult","Minor"]},"GuardianId":{"bsonType":"string","minLength":1,"maxLength":128},"State":{"enum":["Enrolled","Cancelled"]},"Attendance":{"enum":["Unmarked","Present","Absent"]}}}},"Guardians":{"bsonType":"array","minItems":0,"maxItems":8,"items":{"bsonType":"object","required":["_id","Name","Relationship"],"properties":{"_id":{"bsonType":"string","minLength":1,"maxLength":128},"Name":{"bsonType":"string","minLength":1,"maxLength":120},"Relationship":{"bsonType":"string","minLength":1,"maxLength":120}}}},"PricePerLearner":{"bsonType":"decimal","minimum":0}}""");
    public string CalculateChecksum() => MongoMigrationChecksum.Compute(MigrationId + ":v1:session-id:active-slot:date-range:roster:" +
        ClassSchema.ToJson() + ReservationFields.ToJson());

    public async Task UpAsync(IMongoDatabase database, CancellationToken ct)
    {
        const string name = "class_sessions";
        var validator = new BsonDocument
        {
            { "$jsonSchema", ClassSchema }, { "$expr", BsonDocument.Parse("{ $and: [ { $lt: ['$StartUtc','$EndUtc'] }, { $lte: ['$HeldSeats','$Capacity'] } ] }") }
        };
        using var names = await database.ListCollectionNamesAsync(cancellationToken: ct);
        if (!(await names.ToListAsync(ct)).Contains(name))
        {
            await database.CreateCollectionAsync(name, new CreateCollectionOptions<BsonDocument> { Validator = validator }, ct);
        }
        else
        {
            await database.RunCommandAsync<BsonDocument>(new BsonDocument { { "collMod", name }, { "validator", validator } }, cancellationToken: ct);
        }
        var classes = database.GetCollection<BsonDocument>(name);
        var keys = Builders<BsonDocument>.IndexKeys;
        await classes.Indexes.CreateManyAsync([
            new(keys.Ascending("class_session_id"), new CreateIndexOptions { Name = "ux_class_session_id", Unique = true }),
            new(keys.Ascending("StartUtc").Ascending("EndUtc"), new CreateIndexOptions { Name = "ix_class_interval" }),
            new(keys.Ascending("ServiceEntryId").Ascending("StartUtc").Ascending("EndUtc").Ascending("ResourceIds"),
                new CreateIndexOptions<BsonDocument> { Name = "ux_class_active_slot", Unique = true,
                    PartialFilterExpression = new BsonDocument("State", new BsonDocument("$in", new BsonArray { "Open", "Closed" })) })
        ], ct);
        using var metadata = await database.ListCollectionsAsync(new ListCollectionsOptions { Filter = new BsonDocument("name", "reservations") }, ct);
        var existing = (await metadata.SingleAsync(ct))["options"]["validator"].AsBsonDocument;
        existing["$jsonSchema"]["properties"].AsBsonDocument.Merge(ReservationFields, overwriteExistingElements: true);
        var classFields = new BsonArray(ReservationFields.Names);
        var family = new BsonDocument("required", classFields).Add("properties", new BsonDocument("Service", new BsonDocument("enum", new BsonArray { "KpgaGroup" })));
        existing["$jsonSchema"]["oneOf"] = new BsonArray { family, new BsonDocument("not", new BsonDocument("anyOf",
            new BsonArray(ReservationFields.Names.Select(field => new BsonDocument("required", new BsonArray { field }))))) };
        await database.RunCommandAsync<BsonDocument>(new BsonDocument { { "collMod", "reservations" }, { "validator", existing } }, cancellationToken: ct);
        await database.GetCollection<BsonDocument>("reservations").Indexes.CreateOneAsync(new CreateIndexModel<BsonDocument>(
            keys.Ascending("class_session_id").Ascending("_id"), new CreateIndexOptions { Name = "ix_class_roster" }), cancellationToken: ct);
    }
}
