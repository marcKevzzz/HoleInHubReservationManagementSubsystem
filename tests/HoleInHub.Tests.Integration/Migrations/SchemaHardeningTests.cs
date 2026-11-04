using HoleInHub.Data.Context;
using HoleInHub.Data.Migrations;
using HoleInHub.Data.Migrations.Definitions;
using HoleInHub.Data.Reservations;
using HoleInHub.Tests.Integration.Fixtures;
using MongoDB.Bson;
using MongoDB.Driver;
using Xunit;

namespace HoleInHub.Tests.Integration.Migrations;

[Trait("Category", "Integration")]
public sealed class SchemaHardeningTests(MongoDbFixture fixture) : IClassFixture<MongoDbFixture>
{
    private async Task<IMongoDbContext> Setup()
    {
        Assert.True(fixture.IsServerAvailable, "Start the isolated local MongoDB replica set.");
        var context = fixture.CreateDbContext();
        await new Migration_002_Reservations().UpAsync(context.Database, default);
        await new AccountsMigration().UpAsync(context.Database, default);
        await new Migration_004_BookingProcess().UpAsync(context.Database, default);
        await new Migration_005_GoogleAccounts().UpAsync(context.Database, default);
        return context;
    }

    private static IMongoMigration Migration()
    {
        return new Migration_006_SchemaHardening();
    }

    private static ReservationRecord Request(string owner = "account-1") => new()
    {
        AccountId = owner,
        Email = "customer@example.test",
        Reference = Guid.NewGuid().ToString("N"),
        Service = "Regular",
        Status = "Pending",
        Attendees = 1,
        ResourceIds = ["bay-2"],
        StartUtc = DateTime.UtcNow.Date.AddDays(2).AddHours(4),
        EndUtc = DateTime.UtcNow.Date.AddDays(2).AddHours(5),
        CreatedUtc = DateTime.UtcNow,
        ReviewDeadlineUtc = DateTime.UtcNow.AddMinutes(30),
        IdempotencyKey = Guid.NewGuid().ToString("N"),
        SubmissionHash = "hash",
        PolicyVersion = 2,
        History = [new HistoryRecord { Status = "Pending", Actor = "Customer", AtUtc = DateTime.UtcNow }]
    };

    [Theory]
    [InlineData("Attendees", "negative")]
    [InlineData("Status", "unknown")]
    [InlineData("Service", "unknown")]
    [InlineData("ResourceIds", "empty")]
    [InlineData("ResourceIds", "duplicate")]
    [InlineData("StartUtc", "string")]
    [InlineData("EndUtc", "before-start")]
    public async Task ValidatorRejectsMalformedReservations(string field, string value)
    {
        var context = await Setup();
        await Migration().UpAsync(context.Database, default);
        var record = Request().ToBsonDocument();
        record[field] = value switch
        {
            "negative" => new BsonInt32(-1),
            "empty" => new BsonArray(),
            "duplicate" => new BsonArray { "bay-2", "bay-2" },
            "before-start" => record["StartUtc"],
            _ => new BsonString(value)
        };
        var exception = await Assert.ThrowsAsync<MongoWriteException>(() => context.Database.GetCollection<BsonDocument>("reservations").InsertOneAsync(record));
        Assert.Equal(121, exception.WriteError.Code);
    }

    [Fact]
    public async Task UpgradePreservesLegacyOwnershipHistoryAndRerunsSafely()
    {
        var context = await Setup();
        var collection = context.Database.GetCollection<ReservationRecord>("reservations");
        var account = Request();
        var guest = Request("guest:hashed-session");
        await collection.InsertManyAsync([account, guest]);
        await Migration().UpAsync(context.Database, default);
        await Migration().UpAsync(context.Database, default);
        var records = context.Database.GetCollection<BsonDocument>("reservations");
        var savedAccount = await records.Find(new BsonDocument("_id", account.Id)).SingleAsync();
        var savedGuest = await records.Find(new BsonDocument("_id", guest.Id)).SingleAsync();
        Assert.Equal("account-1", savedAccount["CustomerAccountId"].AsString);
        Assert.Equal("guest:hashed-session", savedGuest["AccountId"].AsString);
        Assert.False(savedGuest.Contains("CustomerAccountId"));
        Assert.Equal("Pending", savedAccount["History"][0]["Status"].AsString);
        Assert.NotNull(await collection.Find(x => x.Id == account.Id).SingleAsync());
        var indexes = await records.Indexes.ListAsync();
        Assert.Contains(await indexes.ToListAsync(), x => x["name"] == "ix_reservations_owner_created");
    }

    [Fact]
    public async Task InvalidLegacyDataFailsBeforeChangingValidatorOrDocuments()
    {
        var context = await Setup();
        var record = Request().ToBsonDocument();
        record["Attendees"] = -1;
        var records = context.Database.GetCollection<BsonDocument>("reservations");
        await records.InsertOneAsync(record);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Migration().UpAsync(context.Database, default));
        var unchanged = await records.Find(new BsonDocument("_id", record["_id"])).SingleAsync();
        Assert.Equal(-1, unchanged["Attendees"].AsInt32);
        Assert.False(unchanged.Contains("CustomerAccountId"));
        await records.InsertOneAsync(Request().ToBsonDocument().Set("Attendees", -2));
    }

    [Fact]
    public async Task CurrentWritesKeepGuestRetriesAndStructuredHistory()
    {
        var context = await Setup();
        await Migration().UpAsync(context.Database, default);
        var store = new ReservationPersistence(context);
        var guest = Request("guest:session");
        await store.InsertAsync(guest, default);
        Assert.Equal(guest.Id, (await store.InsertAsync(guest, default)).Id);
        Assert.True(await store.TransitionAsync(guest.Id, "request-cancellation", "Customer", DateTime.UtcNow, [], default));
        var saved = await context.Database.GetCollection<BsonDocument>("reservations").Find(new BsonDocument("_id", guest.Id)).SingleAsync();
        Assert.False(saved.Contains("CustomerAccountId"));
        var history = saved["History"].AsBsonArray.Last().AsBsonDocument;
        Assert.Equal("CancellationRequested", history["EventType"].AsString);
        Assert.Equal("Pending", history["ReservationStatus"].AsString);
        Assert.Equal("Cancellation requested", history["Status"].AsString);
    }

    [Theory]
    [InlineData("accounts", "EmailConfirmed", "wrong-type")]
    [InlineData("resources", "Active", "wrong-type")]
    [InlineData("schedule_blocks", "EndUtc", "before-start")]
    [InlineData("guest_verifications", "Attempts", "negative")]
    [InlineData("outbox", "Attempts", "negative")]
    public async Task OtherCollectionValidatorsRejectBadValues(string name, string field, string value)
    {
        var context = await Setup();
        await Migration().UpAsync(context.Database, default);
        var now = DateTime.UtcNow;
        var record = name switch
        {
            "accounts" => new HoleInHub.Data.Accounts.AccountRecord { Email = "a@example.test", NormalizedEmail = "A@EXAMPLE.TEST" }.ToBsonDocument(),
            "resources" => new ResourceRecord { Id = "bay-test", Name = "Test" }.ToBsonDocument(),
            "schedule_blocks" => new ScheduleBlock { Id = "closure", StartUtc = now, EndUtc = now.AddHours(1) }.ToBsonDocument(),
            "guest_verifications" => new GuestVerificationRecord { Id = "proof", Email = "a@example.test", CodeHash = "hash", SentUtc = now, ExpiresUtc = now.AddMinutes(10) }.ToBsonDocument(),
            _ => new OutboxRecord { Id = "email", Recipient = "a@example.test", DueUtc = now }.ToBsonDocument()
        };
        record[field] = value == "negative" ? new BsonInt32(-1) : value == "before-start" ? new BsonDateTime(now.AddHours(-1)) : new BsonString(value);
        var exception = await Assert.ThrowsAsync<MongoWriteException>(() => context.Database.GetCollection<BsonDocument>(name).InsertOneAsync(record));
        Assert.Equal(121, exception.WriteError.Code);
    }
    [Fact]
    public async Task EmailChallengesHaveExplicitPurposeAndPreserveLegacyBsonNames()
    {
        var context = await Setup();
        await Migration().UpAsync(context.Database, default);
        var verification = new HoleInHub.Business.Accounts.EmailVerificationService(new EmailVerificationPersistence(context));
        foreach (var scope in new[] { "account-email:test-session", "guest-test-session" })
        {
            var hash = HoleInHub.Business.Accounts.EmailVerificationService.CodeHash(scope, "a@example.test", "123456");
            await verification.IssueAsync(scope, "a@example.test", hash, "protected:email", "Verification", scope.StartsWith("account-email:", StringComparison.Ordinal) ? "AccountEmail" : "GuestBooking", default, "protected:context");
            await verification.VerifyAsync(scope, "a@example.test", "123456", default);
            var raw = await context.Database.GetCollection<BsonDocument>("guest_verifications")
                .Find(new BsonDocument("_id", HoleInHub.Business.Accounts.EmailVerificationService.Hash(scope))).SingleAsync();
            Assert.Equal(scope.StartsWith("account-email:", StringComparison.Ordinal) ? "AccountEmail" : "GuestBooking", raw["Purpose"].AsString);
            Assert.Equal("protected:context", raw["ProtectedDraft"].AsString);
            Assert.True(raw.Contains("ConsumedKey"));
            Assert.False(raw.Contains("ProtectedContext"));
            Assert.NotNull((await verification.ReadAsync(scope, default))!.ProtectedContext);
        }
    }

    [Fact]
    public async Task DemoPaymentHistoryRecordsResultingReservationState()
    {
        var context = await Setup();
        await Migration().UpAsync(context.Database, default);
        var store = new ReservationPersistence(context);
        var request = Request();
        request.Service = "WholeShop";
        request.ResourceIds = (await store.CatalogAsync(default)).Select(x => x.Id).ToArray();
        await store.InsertAsync(request, default);
        Assert.True(await store.ApplyDemoPaymentAsync(request.Id, "success", "DEMO-REFERENCE", DateTime.UtcNow, default));
        var saved = await context.Database.GetCollection<BsonDocument>("reservations").Find(new BsonDocument("_id", request.Id)).SingleAsync();
        var entry = saved["History"].AsBsonArray.Last();
        Assert.Equal("DemoPaymentRecorded", entry["EventType"].AsString);
        Assert.Equal("Confirmed", entry["ReservationStatus"].AsString);
        Assert.Equal("account-1", saved["CustomerAccountId"].AsString);
        Assert.True(saved.Contains("PayloadHash"));
        Assert.False(saved.Contains("SubmissionHash"));
    }

    [Fact]
    public async Task PublishedSchemasMatchInstalledValidators()
    {
        var context = await Setup();
        await Migration().UpAsync(context.Database, default);
        var directory = SchemaDirectory();
        using (var before = await context.Database.ListCollectionsAsync(new ListCollectionsOptions { Filter = new BsonDocument("name", "guest_verifications") }))
        {
            var baseline = (await before.SingleAsync())["options"]["validator"]["$jsonSchema"];
            var published = BsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(directory, "email-verification.006.schema.json")));
            Assert.Equal(baseline.ToJson(), published.ToJson());
        }
        await new Migration_007_GuestReservationAccess().UpAsync(context.Database, default);
        var names = new Dictionary<string, string>
        {
            ["reservations"] = "reservation",
            ["accounts"] = "account",
            ["resources"] = "resource",
            ["schedule_blocks"] = "schedule-block",
            ["guest_verifications"] = "email-verification",
            ["outbox"] = "notification",
            ["guest_recovery_quotas"] = "guest-recovery-quota"
        };
        foreach (var (collection, file) in names)
        {
            var result = await context.Database.ListCollectionsAsync(new ListCollectionsOptions { Filter = new BsonDocument("name", collection) });
            var validator = (await result.SingleAsync())["options"]["validator"].AsBsonDocument;
            var schema = BsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(directory, file + ".schema.json")));
            Assert.Equal(validator["$jsonSchema"].ToJson(), schema.ToJson());
            if (validator.Contains("$expr"))
            {
                var published = BsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(directory, file + ".validator.json")));
                Assert.Equal(validator.ToJson(), published.ToJson());
            }
        }
    }

    private static string SchemaDirectory([System.Runtime.CompilerServices.CallerFilePath] string source = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!, "../../../Documentation/schemas"));

    [Fact]
    public async Task ExistingRecoveryQuotaCollectionGetsValidated()
    {
        var context = await Setup();
        await new Migration_006_SchemaHardening().UpAsync(context.Database, default);
        await context.Database.CreateCollectionAsync("guest_recovery_quotas");
        await new Migration_007_GuestReservationAccess().UpAsync(context.Database, default);
        var type = typeof(IMongoMigration).Assembly.GetType("HoleInHub.Data.Migrations.Definitions.Migration_009_RecoveryQuotaValidation");
        Assert.NotNull(type);
        var migration = (IMongoMigration)Activator.CreateInstance(type)!;
        await migration.UpAsync(context.Database, default);
        await migration.UpAsync(context.Database, default);
        var records = context.Database.GetCollection<BsonDocument>("guest_recovery_quotas");
        var error = await Assert.ThrowsAsync<MongoWriteException>(() => records.InsertOneAsync(new BsonDocument("_id", "invalid")));
        Assert.Equal(121, error.WriteError.Code);
        await records.InsertOneAsync(new BsonDocument { { "_id", "valid" }, { "IssuedUtc", new BsonArray() }, { "ExpiresUtc", DateTime.UtcNow.AddHours(1) }, { "Version", 1L } });
    }

}
