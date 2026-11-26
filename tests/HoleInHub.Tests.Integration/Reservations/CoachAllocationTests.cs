using HoleInHub.Data.Migrations;
using HoleInHub.Data.Reservations;
using HoleInHub.Tests.Integration.Fixtures;
using Xunit;
using MongoDB.Driver;
using HoleInHub.Data.Services;

namespace HoleInHub.Tests.Integration.Reservations;

[Trait("Category", "Integration")]
public sealed class CoachAllocationTests(MongoDbFixture fixture) : IClassFixture<MongoDbFixture>
{
    [Fact]
    public async Task PublishedClassHoldsCoachUntilReleaseAndProtectsSchedule()
    {
        var db = fixture.CreateDbContext();
        await new MongoMigrationRunner(db, new MongoMigrationLeaseManager(db), MongoDbFixture.Migrations).RunAsync();
        var store = new ReservationPersistence(db);
        var now = DateTime.UtcNow;
        var start = now.Date.AddDays(15).AddHours(4);
        var coach = new CoachRecord { Id = "class-coach", Name = "Class coach", Active = true,
            WeeklyHours = [new(start.DayOfWeek, 720, 840)] };
        Assert.True(await store.SaveCoachAsync(coach, null, now, default));
        await db.Database.GetCollection<ServiceCatalogRecord>("service_catalog").UpdateOneAsync(x => x.Id == "KpgaGroup",
            Builders<ServiceCatalogRecord>.Update.Set(x => x.CoachIds, new[] { coach.Id }));
        var lesson = await store.PublishClassAsync(new ClassSessionRecord { StartUtc = start, EndUtc = start.AddHours(1),
            ResourceIds = ["bay-2"], Capacity = 3, PricePerLearner = 500, CreatedBy = "staff" }, now, default);
        Assert.Equal(coach.Id, lesson.CoachId);
        var serviceStore = new ServiceCatalogPersistence(db);
        var offering = (await serviceStore.GetAsync("KpgaGroup", default))!;
        offering.CoachIds = [];
        offering.CommandId = Guid.NewGuid().ToString("N");
        await Assert.ThrowsAsync<InvalidOperationException>(() => serviceStore.SaveAsync(offering, offering.Revision, false, [], default));
        Assert.Empty(await store.AvailableCoachesAsync(start, start.AddHours(1), now, default));
        Assert.Single(await store.ReadCoachAssignmentsAsync(now, start.AddDays(1), default));
        Assert.Single(await store.AvailableCoachesAsync(start.AddHours(1), start.AddHours(2), now, default));
        coach.Active = false;
        await Assert.ThrowsAsync<CoachScheduleConflictException>(() => store.SaveCoachAsync(coach, coach.Revision, now, default));
        Assert.True(await store.CloseClassAsync(lesson.ClassSessionId, default));
        Assert.Empty(await store.AvailableCoachesAsync(start, start.AddHours(1), now, default));
        Assert.True(await store.ReleaseClassAsync(lesson.ClassSessionId, default));
        Assert.Single(await store.AvailableCoachesAsync(start, start.AddHours(1), now, default));
        Assert.True(await store.SaveCoachAsync(coach, coach.Revision, now, default));
    }

    [Theory]
    [InlineData("PhilippineCoach")]
    [InlineData("KpgaPro")]
    public async Task DifferentBaysCannotHoldTheSameCoachAtTheSameTime(string competingService)
    {
        var db = fixture.CreateDbContext();
        await new MongoMigrationRunner(db, new MongoMigrationLeaseManager(db), MongoDbFixture.Migrations).RunAsync();
        var store = new ReservationPersistence(db);
        var now = DateTime.UtcNow;
        var start = now.Date.AddDays(8).AddHours(4);
        var coach = new CoachRecord
        {
            Id = "coach-one",
            Name = "Maria",
            Active = true,
            WeeklyHours = [new(start.DayOfWeek, 720, 780)]
        };
        Assert.True(await store.SaveCoachAsync(coach, null, now, default));
        Assert.Empty(await store.AvailableCoachesAsync(start, start.AddMinutes(61), now, default));
        async Task<ReservationRecord?> Claim(string bay)
        {
            try
            {
                return await store.InsertAsync(new ReservationRecord
                {
                    Reference = "HIH-" + Guid.NewGuid().ToString("N")[..8],
                    AccountId = bay,
                    FirstName = "Ana",
                    LastName = "Cruz",
                    Email = bay + "@example.test",
                    Phone = "09123456789",
                    Service = bay == "bay-1" ? "PhilippineCoach" : competingService,
                    ResourceIds = [bay],
                    Attendees = 1,
                    StartUtc = start,
                    EndUtc = start.AddHours(1),
                    CreatedUtc = now,
                    ReviewDeadlineUtc = now.AddHours(1),
                    SubmissionHash = "coach-allocation-test",
                    Status = "Pending",
                    IdempotencyKey = Guid.NewGuid().ToString("N")
                }, default, requireCoach: true);
            }
            catch (AllocationConflictException) { return null; }
        }
        var claims = await Task.WhenAll(Claim("bay-1"), Claim("bay-2"));
        var winner = Assert.Single(claims.OfType<ReservationRecord>());
        Assert.Equal(coach.Id, winner.CoachId);
        coach.WeeklyHours = [new(start.DayOfWeek, 720, 750)];
        var shortened = await Assert.ThrowsAsync<CoachScheduleConflictException>(() => store.SaveCoachAsync(coach, coach.Revision, now, default));
        Assert.Contains(winner.Id, shortened.ReservationIds);
        coach.Active = false;
        var conflict = await Assert.ThrowsAsync<CoachScheduleConflictException>(() => store.SaveCoachAsync(coach, coach.Revision, now, default));
        Assert.Contains(winner.Id, conflict.ReservationIds);
        Assert.Single(await store.AvailableCoachesAsync(start, start.AddHours(1), now.AddHours(2), default));

        var other = new CoachRecord
        {
            Id = "coach-two",
            Name = "Ana",
            Active = true,
            WeeklyHours = [new(start.DayOfWeek, 720, 780)]
        };
        Assert.True(await store.SaveCoachAsync(other, null, now, default));
        var changes = await Task.WhenAll(
            store.AssignCoachAsync(winner.Id, winner.Revision, other.Id, "staff-one", now, default),
            store.AssignCoachAsync(winner.Id, winner.Revision, other.Id, "staff-two", now, default));
        Assert.Single(changes, saved => saved);
        Assert.Equal(other.Name, (await store.ByIdAsync(winner.Id, default))!.CoachName);
        Assert.Equal(coach.Id, Assert.Single(await store.AvailableCoachesAsync(start, start.AddHours(1), now, default)).Id);
        Assert.True(await store.SaveCoachAsync(coach, coach.Revision, now, default));
    }
}
