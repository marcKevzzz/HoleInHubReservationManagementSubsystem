using HoleInHub.Data.Reservations;
using MongoDB.Bson;
using MongoDB.Driver;

namespace HoleInHub.Data.Migrations.Definitions;

public sealed class Migration_006_SchemaHardening : IMongoMigration
{
    public string MigrationId => "006_SchemaHardening";
    public string Description => "Typed validators, customer account links and customer history index";
    public string CalculateChecksum() => MongoMigrationChecksum.Compute(MigrationId + ":v1:" + Validators().ToJson());

    public async Task UpAsync(IMongoDatabase database, CancellationToken ct)
    {
        var validators = Validators();
        // Preflight every collection before changing data: invalid legacy values need an explicit repair decision.
        foreach (var item in validators)
        {
            var invalid = new BsonDocument("$nor", new BsonArray { item.Value });
            if (await database.GetCollection<BsonDocument>(item.Name).Find(invalid).Limit(1).AnyAsync(ct))
            {
                throw new InvalidOperationException($"Collection '{item.Name}' contains documents incompatible with migration 006. Review and repair them before retrying; no automatic deletion or invented values are applied.");
            }
        }

        foreach (var item in validators)
        {
            await database.RunCommandAsync<BsonDocument>(new BsonDocument
            {
                { "collMod", item.Name }, { "validator", item.Value },
                { "validationLevel", "strict" }, { "validationAction", "error" }
            }, cancellationToken: ct);
        }

        var reservations = database.GetCollection<BsonDocument>("reservations");
        var accountOwners = new BsonDocument
        {
            { "CustomerAccountId", new BsonDocument("$exists", false) },
            { "AccountId", new BsonDocument { { "$type", "string" }, { "$not", new BsonRegularExpression("^guest:") } } }
        };
        await reservations.UpdateManyAsync(accountOwners,
            new PipelineUpdateDefinition<BsonDocument>(new[] { new BsonDocument("$set", new BsonDocument("CustomerAccountId", "$AccountId")) }), cancellationToken: ct);
        await reservations.Indexes.CreateOneAsync(new CreateIndexModel<BsonDocument>(
            Builders<BsonDocument>.IndexKeys.Ascending("AccountId").Descending("CreatedUtc"),
            new CreateIndexOptions { Name = "ix_reservations_owner_created" }), cancellationToken: ct);
    }

    private static BsonDocument Validators()
    {
        var states = new[] { "Pending", "Confirmed", "Checked-In", "Completed", "Cancelled", "Rejected", "Expired", "No-show" };
        var reservation = Schema(["_id", "AccountId", "Email", "Reference", "Service", "Status", "StartUtc", "EndUtc", "CreatedUtc", "ReviewDeadlineUtc", "ResourceIds", "Attendees", "IdempotencyKey", "PayloadHash"], new BsonDocument
        {
            { "_id", Text(1) }, { "AccountId", Text(1) }, { "CustomerAccountId", Text(1) },
            { "Email", Text(1, 254) }, { "Reference", Text(1) },
            { "Service", Enum("Regular", "VIP", "PhilippineCoach", "KpgaPro", "KpgaGroup", "WholeShop") },
            { "Status", Enum(states) }, { "StartUtc", Type("date") }, { "EndUtc", Type("date") },
            { "CreatedUtc", Type("date") }, { "ReviewDeadlineUtc", Type("date") },
            { "ResourceIds", Strings(1, true) }, { "ResourceNames", Type("object") },
            { "FirstName", Text(0, 100) }, { "LastName", Text(0, 100) }, { "Phone", Text(0, 30) },
            { "Attendees", Integer(1) }, { "Notes", Text(0, 1000) }, { "PrivacyAccepted", Type("bool") },
            { "PolicyVersion", Integer(0) }, { "GuestSessionHash", Type("string", "null") }, { "AccessTokenHash", Type("string", "null") },
            { "IdempotencyKey", Text(1, 100) }, { "PayloadHash", Text(1) },
            { "PaymentStatus", Enum("NotRequired", "AwaitingDeposit", "DepositReceived", "LatePayment", "Failed", "CheckoutCancelled") },
            { "PaymentReference", Type("string", "null") }, { "RentalTotal", Money() }, { "DepositAmount", Money() },
            { "CoachConfirmed", Type("bool") }, { "CancellationRequested", Type("bool") },
            { "Participants", new BsonDocument { { "bsonType", "array" }, { "items", Schema(["ResourceId", "Players", "NonPlayers"], new BsonDocument
                { { "ResourceId", Text(1) }, { "Players", Integer(0) }, { "NonPlayers", Integer(0) } }) } } },
            { "History", new BsonDocument { { "bsonType", "array" }, { "items", Schema(["Status", "AtUtc", "Actor"], new BsonDocument
                {
                    { "Status", Text() }, { "AtUtc", Type("date") }, { "Actor", Text() }, { "Reason", Text(0, 1000) },
                    { "EventType", Enum("SubmissionCreated", "ReservationConfirmed", "ReservationRejected", "CoachConfirmed", "CancellationRequested", "ReservationCancelled", "CancellationDeclined", "ReservationExpired", "DemoPaymentRecorded") },
                    { "ReservationStatus", Enum(states) }
                }) } } }
        });
        return new BsonDocument
        {
            { "reservations", Interval(reservation) },
            { "accounts", new BsonDocument("$jsonSchema", Schema(["_id", "Email", "NormalizedEmail"], new BsonDocument
                {
                    { "_id", Text(1) }, { "Email", Text(1, 254) }, { "NormalizedEmail", Text(1, 254) },
                    { "FirstName", Text(0, 100) }, { "LastName", Text(0, 100) }, { "Phone", Text(0, 30) },
                    { "GoogleSubject", Text(1) }, { "PasswordHash", Text() }, { "SecurityStamp", Text(1) }, { "Version", Text(1) },
                    { "EmailConfirmed", Type("bool") }, { "AccessFailedCount", Integer(0) }, { "LockoutEndUtc", Type("date", "null") }, { "Roles", Strings(0, true) }
                })) },
            { "resources", new BsonDocument("$jsonSchema", Schema(["_id", "Name", "Service"], new BsonDocument
                { { "_id", Text(1) }, { "Name", Text(1) }, { "Service", Enum("Regular", "VIP") }, { "Active", Type("bool") } })) },
            { "schedule_blocks", Interval(Schema(["_id", "ResourceIds", "StartUtc", "EndUtc"], new BsonDocument
                { { "_id", Text(1) }, { "ResourceIds", Strings(0, true) }, { "StartUtc", Type("date") }, { "EndUtc", Type("date") } })) },
            { "guest_verifications", new BsonDocument("$jsonSchema", Schema(["_id", "Email", "CodeHash", "SentUtc", "ExpiresUtc"], new BsonDocument
                {
                    { "_id", Text(1) }, { "Email", Text(1, 254) }, { "CodeHash", Text(1) },
                    { "SentUtc", Type("date") }, { "ExpiresUtc", Type("date") }, { "Attempts", Integer(0) },
                    { "VerifiedUtc", Type("date", "null") }, { "ConsumedKey", Type("string", "null") }, { "ProtectedDraft", Type("string", "null") },
                    { "Purpose", Enum("GuestBooking", "AccountEmail") }
                })) },
            { "outbox", new BsonDocument("$jsonSchema", Schema(["_id", "Recipient", "DueUtc"], new BsonDocument
                {
                    { "_id", Text(1) }, { "Recipient", Text(1, 254) }, { "Subject", Text() }, { "Body", Text() },
                    { "ReservationId", Type("string", "null") }, { "RequiredStatus", Type("string", "null") },
                    { "DueUtc", Type("date") }, { "LeaseUntilUtc", Type("date", "null") }, { "LeaseToken", Type("string", "null") },
                    { "SentUtc", Type("date", "null") }, { "Attempts", Integer(0) }, { "Failed", Type("bool") }
                })) }
        };
    }

    private static BsonDocument Schema(string[] required, BsonDocument properties) => new()
    { { "bsonType", "object" }, { "required", new BsonArray(required) }, { "properties", properties } };
    private static BsonDocument Type(params string[] types) => new("bsonType", types.Length == 1 ? new BsonString(types[0]) : new BsonArray(types));
    private static BsonDocument Text(int minimum = 0, int? maximum = null)
    {
        var value = Type("string").Add("minLength", minimum);
        if (maximum is { } limit) { value.Add("maxLength", limit); }
        return value;
    }
    private static BsonDocument Integer(int minimum) => Type("int").Add("minimum", minimum);
    private static BsonDocument Enum(params string[] values) => new("enum", new BsonArray(values));
    private static BsonDocument Strings(int minimum, bool unique) => new()
    { { "bsonType", "array" }, { "minItems", minimum }, { "uniqueItems", unique }, { "items", Text(1) } };
    private static BsonDocument Money() => Type("decimal", "double", "int", "long", "null").Add("minimum", 0);
    private static BsonDocument Interval(BsonDocument schema) => new()
    { { "$jsonSchema", schema }, { "$expr", new BsonDocument("$lt", new BsonArray { "$StartUtc", "$EndUtc" }) } };
}
