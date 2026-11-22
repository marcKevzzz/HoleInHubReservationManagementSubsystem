using HoleInHub.Business.Services;
using HoleInHub.Data;
using HoleInHub.Data.Accounts;
using HoleInHub.Data.Customers;
using HoleInHub.Data.Migrations;
using HoleInHub.Data.Migrations.Definitions;
using HoleInHub.Data.Reservations;
using HoleInHub.Data.Services;
using HoleInHub.Data.Settings;
using HoleInHub.Tests.Integration.Fixtures;
using MongoDB.Bson;
using MongoDB.Driver;
using Xunit;

namespace HoleInHub.Tests.Integration.Migrations;

[Trait("Category", "Integration")]
public sealed class BusinessIdentifierMigrationTests(MongoDbFixture fixture) : IClassFixture<MongoDbFixture>
{
    [Fact]
    public async Task UpgradePreservesLegacyDocumentsAndRequiresMatchingUniqueIdentifiers()
    {
        var context = fixture.CreateDbContext();
        var db = context.Database;
        var migration = new Migration_021_BusinessIdentifiers();
        var leases = new MongoMigrationLeaseManager(context);
        var earlier = MongoDbFixture.Migrations.Where(m => string.CompareOrdinal(m.MigrationId, migration.MigrationId) < 0);
        await new MongoMigrationRunner(context, leases, earlier).RunAsync();
        var health = new HealthService(context);
        Assert.False((await health.CheckDatabaseReadinessAsync()).IsReady);

        var now = DateTime.UtcNow;
        var booking = new ReservationRecord
        {
            Reference = "HIH-OLD-123",
            AccountId = "account",
            CustomerAccountId = "account",
            Email = "one@example.test",
            Service = "Regular",
            StartUtc = now,
            EndUtc = now.AddHours(1),
            CreatedUtc = now,
            ReviewDeadlineUtc = now,
            Attendees = 1,
            ResourceIds = ["bay-2"],
            IdempotencyKey = "submission",
            SubmissionHash = "hash"
        };
        (string Collection, string Field, BsonDocument Document)[] legacy =
        [
            ("reservations", "reservation_id", booking.ToBsonDocument()),
            ("customer_profiles", "customer_id", new CustomerProfileRecord { Id = "profile", RepresentativeBookingId = booking.Id }.ToBsonDocument()),
            ("accounts", "account_id", new AccountRecord { Id = "account", Email = booking.Email, NormalizedEmail = booking.Email.ToUpperInvariant() }.ToBsonDocument()),
            ("resources", "resource_id", new ResourceRecord { Id = "legacy-bay", Name = "Legacy bay" }.ToBsonDocument()),
            ("service_catalog", "service_id", (await db.GetCollection<BsonDocument>("service_catalog").Find(new BsonDocument("_id", "Regular")).SingleAsync()).Set("_id", "legacy-service")),
            ("schedule_blocks", "block_id", new ScheduleBlock { Id = "closure", ResourceIds = ["bay-2"], StartUtc = now, EndUtc = now.AddHours(1) }.ToBsonDocument()),
            ("service_images", "service_image_id", new ServiceImageRecord { Id = "photo", ContentType = "image/png", Content = [1, 2, 3] }.ToBsonDocument()),
            ("system_settings", "settings_id", new SystemSettingsRecord { UpdatedUtc = now }.ToBsonDocument())
        ];
        foreach (var (collection, field, document) in legacy)
        {
            document.Remove(field);
            await db.GetCollection<BsonDocument>(collection).InsertOneAsync(document);
        }

        var runner = new MongoMigrationRunner(context, leases, earlier.Append(migration));
        Assert.Equal(1, (await runner.RunAsync()).NewlyApplied);
        Assert.False((await health.CheckDatabaseReadinessAsync()).IsReady);
        await migration.UpAsync(db, default);
        Assert.Equal(0, (await runner.RunAsync()).NewlyApplied);
        using var scheduleIndexes = await db.GetCollection<BsonDocument>("reservations").Indexes.ListAsync();
        Assert.Contains(await scheduleIndexes.ToListAsync(), index => index["name"] == "ix_reservations_start_id" &&
            index["key"] == new BsonDocument { { "StartUtc", 1 }, { "_id", 1 } });
        foreach (var (collection, field, document) in legacy)
        {
            var records = db.GetCollection<BsonDocument>(collection);
            var key = new BsonDocument("_id", document["_id"]);
            var saved = await records.Find(key).SingleAsync();
            Assert.Equal(collection == "reservations" ? document["Reference"] : document["_id"], saved[field]);
            saved.Remove(field);
            Assert.Equal(document.OrderBy(element => element.Name), saved.OrderBy(element => element.Name));
            using var indexes = await records.Indexes.ListAsync();
            Assert.Contains(await indexes.ToListAsync(), index =>
                index["key"] == new BsonDocument(field, 1) && index.GetValue("unique", false) == true);
            foreach (var change in new[] { Builders<BsonDocument>.Update.Unset(field), Builders<BsonDocument>.Update.Set(field, "another-id") })
            {
                var error = await Assert.ThrowsAsync<MongoWriteException>(() => records.UpdateOneAsync(key, change));
                Assert.Equal(121, error.WriteError.Code);
            }
        }
        booking.Id = "second-booking";
        booking.IdempotencyKey = "another-submission";
        var duplicate = await Assert.ThrowsAsync<MongoWriteException>(() => db.GetCollection<ReservationRecord>("reservations").InsertOneAsync(booking));
        Assert.Equal(ServerErrorCategory.DuplicateKey, duplicate.WriteError.Category);

        var prefixed = new Migration_022_PrefixedBusinessIdentifiers();
        runner = new MongoMigrationRunner(context, leases, earlier.Append(migration).Append(prefixed));
        Assert.Equal(1, (await runner.RunAsync()).NewlyApplied);
        Assert.True((await health.CheckDatabaseReadinessAsync()).IsReady);

        // Simulate interruption after installing a validator but before updating its last legacy record.
        await db.GetCollection<BsonDocument>("service_images").UpdateOneAsync(new BsonDocument("_id", "photo"),
            new BsonDocument("$set", new BsonDocument("service_image_id", "photo")), new UpdateOptions { BypassDocumentValidation = true });
        await prefixed.UpAsync(db, default);
        Assert.Equal(0, (await runner.RunAsync()).NewlyApplied);
        foreach (var (collection, field, document) in legacy)
        {
            var records = db.GetCollection<BsonDocument>(collection);
            var key = document["_id"].AsString;
            var saved = await records.Find(new BsonDocument("_id", key)).SingleAsync();
            var expected = collection switch
            {
                "reservations" => document["Reference"].AsString,
                "resources" => key,
                "accounts" => BusinessIdentifier.Format("ACC", key),
                "customer_profiles" => BusinessIdentifier.Format("CUS", key),
                "service_catalog" => BusinessIdentifier.Format("SVC", key),
                "schedule_blocks" => BusinessIdentifier.Format("BLK", key),
                "service_images" => BusinessIdentifier.Format("IMG", key),
                "system_settings" => BusinessIdentifier.Format("SET", key),
                _ => throw new ArgumentOutOfRangeException(nameof(collection))
            };
            Assert.Equal(expected, saved[field].AsString);
            saved.Remove(field);
            Assert.Equal(document.OrderBy(element => element.Name), saved.OrderBy(element => element.Name));
            var error = await Assert.ThrowsAsync<MongoWriteException>(() => records.UpdateOneAsync(new BsonDocument("_id", key),
                new BsonDocument("$set", new BsonDocument(field, "invalid"))));
            Assert.Equal(121, error.WriteError.Code);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PrefixedCodePreflightPreservesEarlierCollectionsOnConflict(bool occupied)
    {
        var db = fixture.CreateDbContext().Database;
        foreach (var migration in MongoDbFixture.Migrations.TakeWhile(m => m is not Migration_022_PrefixedBusinessIdentifiers))
        {
            await migration.UpAsync(db, default);
        }
        var images = db.GetCollection<BsonDocument>("service_images");
        await images.InsertOneAsync(new BsonDocument { { "_id", "photo" }, { "service_image_id", "photo" } });
        var key = occupied ? BusinessIdentifier.Format("IMG", "photo") : "second-photo";
        await images.InsertOneAsync(new BsonDocument { { "_id", key }, { "service_image_id", occupied ? key : "IMG-0000000000000000" } },
            new InsertOneOptions { BypassDocumentValidation = true });
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => new Migration_022_PrefixedBusinessIdentifiers().UpAsync(db, default));
        Assert.Contains("service_images.service_image_id", failure.Message);
        var service = await db.GetCollection<BsonDocument>("service_catalog").Find(new BsonDocument("_id", "Regular")).SingleAsync();
        Assert.Equal("Regular", service["service_id"].AsString);
        var error = await Assert.ThrowsAsync<MongoWriteException>(() => db.GetCollection<BsonDocument>("service_catalog").UpdateOneAsync(
            new BsonDocument("_id", "Regular"), new BsonDocument("$set", new BsonDocument("service_id", BusinessIdentifier.Format("SVC", "Regular")))));
        Assert.Equal(121, error.WriteError.Code);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("blank")]
    [InlineData("conflicting")]
    [InlineData("non-string")]
    public async Task InvalidLegacyIdentifiersStopBeforeChangingAnyCollection(string problem)
    {
        var db = fixture.CreateDbContext().Database;
        foreach (var name in new[] { "reservations", "customer_profiles", "accounts", "resources", "service_catalog", "schedule_blocks", "system_settings" })
        {
            await db.CreateCollectionAsync(name);
        }
        var bookings = db.GetCollection<BsonDocument>("reservations");
        await bookings.InsertOneAsync(new BsonDocument { { "_id", "first" }, { "Reference", "HIH-one" } });
        switch (problem)
        {
            case "missing": await bookings.InsertOneAsync(new BsonDocument("_id", "other")); break;
            case "duplicate": await bookings.InsertOneAsync(new BsonDocument { { "_id", "other" }, { "Reference", "HIH-one" } }); break;
            case "blank": await bookings.InsertOneAsync(new BsonDocument { { "_id", "other" }, { "Reference", "   " } }); break;
            case "conflicting": await db.GetCollection<BsonDocument>("system_settings").InsertOneAsync(new BsonDocument { { "_id", "venue" }, { "settings_id", "different" } }); break;
            case "non-string": await db.GetCollection<BsonDocument>("service_images").InsertOneAsync(new BsonDocument("_id", 123)); break;
        }
        await Assert.ThrowsAsync<InvalidOperationException>(() => new Migration_021_BusinessIdentifiers().UpAsync(db, default));
        Assert.False((await bookings.Find(new BsonDocument("_id", "first")).SingleAsync()).Contains("reservation_id"));
        using var collections = await db.ListCollectionsAsync(new ListCollectionsOptions { Filter = new BsonDocument("name", "reservations") });
        Assert.False((await collections.SingleAsync())["options"].AsBsonDocument.Contains("validator"));
        using var indexes = await bookings.Indexes.ListAsync();
        Assert.Single(await indexes.ToListAsync());
    }
}
