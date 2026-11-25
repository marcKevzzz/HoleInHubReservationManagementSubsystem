using HoleInHub.Data.Migrations;
using HoleInHub.Data.Reservations;
using HoleInHub.Tests.Integration.Fixtures;
using Xunit;

namespace HoleInHub.Tests.Integration.Reservations;

[Trait("Category", "Integration")]
public sealed class CoachAllocationTests(MongoDbFixture fixture) : IClassFixture<MongoDbFixture>
{
    [Fact]
    public async Task DifferentBaysCannotHoldTheSameCoachAtTheSameTime()
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
                    Service = "PhilippineCoach",
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
