using HoleInHub.Data;
using HoleInHub.Data.Accounts;
using HoleInHub.Data.Customers;
using HoleInHub.Data.Reservations;
using HoleInHub.Data.Services;
using HoleInHub.Data.Settings;
using HoleInHub.Tests.Integration.Fixtures;
using MongoDB.Bson;
using MongoDB.Driver;
using Xunit;

namespace HoleInHub.Tests.Integration;

[Trait("Category", "Integration")]
public sealed class NamedIdentifierWriteTests(MongoDbFixture fixture) : IClassFixture<MongoDbFixture>
{
    [Fact]
    public async Task HistoricalCatalogSeedsAndGroupedCustomerBackfillSurviveTheEntireUpgrade()
    {
        var db = fixture.CreateDbContext().Database;
        var now = DateTime.UtcNow;
        foreach (var migration in MongoDbFixture.Migrations)
        {
            if (migration.MigrationId == "018_CustomerProfiles")
            {
                var reservations = db.GetCollection<ReservationRecord>("reservations");
                foreach (var key in new[] { "first", "second" })
                {
                    await reservations.InsertOneAsync(new ReservationRecord
                    {
                        AccountId = "legacy",
                        Email = "customer@example.test",
                        Reference = "HIH-" + key,
                        Service = "Regular",
                        ResourceIds = ["bay-2"],
                        Attendees = 1,
                        CreatedUtc = now,
                        ReviewDeadlineUtc = now.AddMinutes(30),
                        StartUtc = now.AddDays(1),
                        EndUtc = now.AddDays(1).AddHours(1),
                        IdempotencyKey = key,
                        SubmissionHash = "hash"
                    });
                }
            }
            await migration.UpAsync(db, default);
        }

        var profile = await db.GetCollection<BsonDocument>("customer_profiles").Find(FilterDefinition<BsonDocument>.Empty).SingleAsync();
        Assert.Equal(BusinessIdentifier.Format("CUS", profile["_id"].AsString), profile["customer_id"].AsString);
        Assert.Equal(2, await db.GetCollection<BsonDocument>("customer_profile_bookings").CountDocumentsAsync(new BsonDocument("ProfileId", profile["_id"])));
        var service = await db.GetCollection<BsonDocument>("service_catalog").Find(new BsonDocument("_id", "Regular")).SingleAsync();
        Assert.Equal(BusinessIdentifier.Format("SVC", "Regular"), service["service_id"].AsString);
    }

    [Fact]
    public async Task NormalWritesKeepNamedIdsAndExistingKeysAfterMigration()
    {
        Assert.True(fixture.IsServerAvailable, "Start the isolated local MongoDB replica set.");
        var context = fixture.CreateDbContext();
        foreach (var migration in MongoDbFixture.Migrations) { await migration.UpAsync(context.Database, default); }

        var accounts = new AccountPersistence(context);
        var account = new AccountRecord { Email = "customer@example.test", NormalizedEmail = "CUSTOMER@EXAMPLE.TEST" };
        Assert.True(await accounts.CreateAsync(account, default));
        account.FirstName = "Updated";
        Assert.True(await accounts.UpdateAsync(account, account.Version, default));

        var store = new ReservationPersistence(context);
        var resource = new ResourceRecord { Id = "practice", Name = "Practice bay" };
        await store.AddResourceAsync(resource, default);
        var now = DateTime.UtcNow;
        var booking = await store.InsertAsync(new ReservationRecord
        {
            Reference = "HIH-EXAMPLE",
            AccountId = account.Id,
            Email = account.Email,
            Service = "Regular",
            ResourceIds = [resource.Id],
            Attendees = 1,
            CreatedUtc = now,
            ReviewDeadlineUtc = now.AddMinutes(30),
            StartUtc = now.AddDays(1),
            EndUtc = now.AddDays(1).AddHours(1),
            IdempotencyKey = "submission",
            SubmissionHash = "hash"
        }, default);
        var profile = (await new CustomerProfilePersistence(context).ReadAsync(booking.Id, default))!;
        var block = await store.InsertBlockAsync(new ScheduleBlock
        {
            Id = "maintenance",
            ResourceIds = [resource.Id],
            StartUtc = booking.EndUtc,
            EndUtc = booking.EndUtc.AddHours(1)
        }, default);

        var catalog = new ServiceCatalogPersistence(context);
        var service = (await catalog.GetAsync("Regular", default))!;
        service.CommandId = "edit";
        var image = new ServiceImageRecord { Id = "practice-image", ContentType = "image/png", Content = [1, 2, 3] };
        await catalog.SaveAsync(service, service.Revision, false, [image], default);

        var settings = new SystemSettingsPersistence(context);
        await settings.SaveAsync(new SystemSettingsRecord { CommandId = "save", UpdatedUtc = now }, -1, default);

        await Check("accounts", account.Id, "account_id", BusinessIdentifier.Format("ACC", account.Id));
        await Check("resources", resource.Id, "resource_id", resource.Id);
        await Check("reservations", booking.Id, "reservation_id", booking.Reference);
        await Check("customer_profiles", profile.Profile.Id, "customer_id", BusinessIdentifier.Format("CUS", profile.Profile.Id));
        await Check("schedule_blocks", block.Id, "block_id", BusinessIdentifier.Format("BLK", block.Id));
        await Check("service_catalog", service.Id, "service_id", BusinessIdentifier.Format("SVC", service.Id));
        await Check("service_images", image.Id, "service_image_id", BusinessIdentifier.Format("IMG", image.Id));
        await Check("system_settings", "venue", "settings_id", BusinessIdentifier.Format("SET", "venue"));

        async Task Check(string collection, string id, string field, string expected)
        {
            var record = await context.Database.GetCollection<BsonDocument>(collection).Find(new BsonDocument("_id", id)).SingleAsync();
            Assert.Equal(expected, record[field].AsString);
        }
    }
}
