using HoleInHub.Data.Migrations;
using HoleInHub.Data.Reservations;
using HoleInHub.Tests.Integration.Fixtures;
using MongoDB.Driver;
using Xunit;

namespace HoleInHub.Tests.Integration.Reservations;

[Trait("Category", "Integration")]
public sealed class SharedClassTests(MongoDbFixture fixture) : IClassFixture<MongoDbFixture>
{
    [Fact]
    public async Task CompetingFamiliesClaimBothSeatsOrNeitherAndCancellationReturnsOneSeatOnce()
    {
        var db = fixture.CreateDbContext();
        await new MongoMigrationRunner(db, new MongoMigrationLeaseManager(db), MongoDbFixture.Migrations).RunAsync();
        var store = new ReservationPersistence(db);
        var now = DateTime.UtcNow;
        var start = now.Date.AddDays(15).AddHours(7);
        var session = await store.PublishClassAsync(new ClassSessionRecord
        {
            StartUtc = start,
            EndUtc = start.AddHours(1),
            ResourceIds = ["bay-2"],
            Capacity = 3,
            PricePerLearner = 500,
            CreatedBy = "staff"
        }, now, default);
        async Task<ReservationRecord?> Claim(string family)
        {
            var booking = new ReservationRecord
            {
                AccountId = family,
                Reference = "HIH-" + family,
                Service = "KpgaGroup",
                Email = family + "@example.test",
                Phone = "09123456789",
                FirstName = "Family",
                LastName = family,
                CreatedUtc = now,
                ReviewDeadlineUtc = now.AddHours(1),
                ClassSessionId = session.ClassSessionId,
                StartUtc = start,
                EndUtc = start.AddHours(1),
                PricePerLearner = 500,
                RentalTotal = 1000,
                ResourceIds = ["bay-2"],
                Attendees = 2,
                Status = "Pending",
                IdempotencyKey = Guid.NewGuid().ToString("N"),
                SubmissionHash = "family-test",
                Learners = [new() { Id = "one", Name = "Alex", AgeCategory = "Minor", GuardianId = "parent" },
                    new() { Id = "two", Name = "Sam", AgeCategory = "Minor", GuardianId = "parent" }],
                Guardians = [new() { Id = "parent", Name = "Parent", Relationship = "Parent" }]
            };
            try { return await store.InsertAsync(booking, default); }
            catch (AllocationConflictException) { return null; }
        }
        var result = await Task.WhenAll(Claim("First"), Claim("Second"));
        var winner = Assert.Single(result.OfType<ReservationRecord>());
        Assert.True(await store.SetClassReminderAsync(winner.Id, "https://venue.example", default));
        var next = new ClassSessionRecord
        {
            StartUtc = start.AddDays(7),
            EndUtc = start.AddDays(7).AddHours(1),
            ResourceIds = ["bay-2"],
            Capacity = 1,
            PricePerLearner = 500,
            CreatedBy = "manager"
        };
        await store.PublishClassAsync(next, now, default);
        await store.PublishClassAsync(next, now, default);
        Assert.False(await db.Database.GetCollection<OutboxRecord>("outbox")
            .Find(x => x.Id == winner.Id + ":next-class").AnyAsync());
        Assert.Equal(2, (await store.ClassAsync(session.ClassSessionId, default))!.HeldSeats);
        Assert.True(await store.CancelLearnerAsync(winner.Id, "one", "Customer", now, default));
        Assert.True(await store.CancelLearnerAsync(winner.Id, "one", "Customer", now, default));
        Assert.Equal(1, (await store.ClassAsync(session.ClassSessionId, default))!.HeldSeats);
        Assert.Single((await store.ByIdAsync(winner.Id, default))!.Learners!, x => x.State == "Enrolled");
    }

    [Fact]
    public async Task FourLessonsClaimEverySeatAndApproveOneAbsence()
    {
        var db = fixture.CreateDbContext();
        await new MongoMigrationRunner(db, new MongoMigrationLeaseManager(db), MongoDbFixture.Migrations).RunAsync();
        var store = new ReservationPersistence(db);
        var now = DateTime.UtcNow;
        var start = now.Date.AddDays(15).AddHours(7);
        var sessions = new List<ClassSessionRecord>();
        for (int i = 0; i < 4; i++)
        {
            sessions.Add(await store.PublishClassAsync(new ClassSessionRecord
            {
                StartUtc = start.AddDays(i * 7),
                EndUtc = start.AddDays(i * 7).AddHours(1),
                ResourceIds = ["bay-2"],
                PricePerLearner = 500,
                Capacity = i == 2 ? 3 : 4,
                CreatedBy = "manager"
            }, now, default));
        }
        var pack = await store.PublishPackageAsync("KpgaGroup", sessions.Select(x => x.ClassSessionId).ToArray(), HoleInHub.Business.Reservations.PricingRules.Default("KpgaGroup"), now, default);
        async Task<ReservationRecord?> Claim(string family, DateTime? submitted = null)
        {
            var booking = new ReservationRecord
            {
                AccountId = family,
                Reference = "HIH-" + family,
                Service = "KpgaGroup",
                Email = family + "@example.test",
                Phone = "09123456789",
                FirstName = "Family",
                LastName = family,
                CreatedUtc = submitted ?? now,
                ReviewDeadlineUtc = (submitted ?? now).AddHours(1),
                ClassSessionId = sessions[0].ClassSessionId,
                PackageId = pack.PackageId,
                StartUtc = start,
                EndUtc = start.AddHours(1),
                PricePerLearner = pack.PricePerLearner,
                RentalTotal = pack.PricePerLearner * 2,
                ResourceIds = ["bay-2"],
                Attendees = 2,
                Status = "Pending",
                IdempotencyKey = Guid.NewGuid().ToString("N"),
                SubmissionHash = "family-test",
                Learners = [new() { Id = "one", Name = "Alex", AgeCategory = "Minor", GuardianId = "parent" },
                    new() { Id = "two", Name = "Sam", AgeCategory = "Minor", GuardianId = "parent" }],
                Guardians = [new() { Id = "parent", Name = "Parent", Relationship = "Parent" }]
            };
            try { return await store.InsertAsync(booking, default); }
            catch (AllocationConflictException) { return null; }
        }

        var cutoff = pack.RegistrationEndsUtc!.Value;
        Assert.Null(await Claim("Late", cutoff));
        var lastMinute = Assert.IsType<ReservationRecord>(await Claim("LastMinute", cutoff.AddMinutes(-1)));
        await store.ExpireAsync(lastMinute.ReviewDeadlineUtc, [], default);
        Assert.Equal("Expired", (await store.ByIdAsync(lastMinute.Id, default))!.Status);
        foreach (var lesson in sessions) { Assert.Equal(0, (await store.ClassAsync(lesson.ClassSessionId, default))!.HeldSeats); }

        var booking = Assert.IsType<ReservationRecord>(await Claim("First"));
        Assert.Null(await Claim("Second"));
        foreach (var lesson in sessions)
        {
            Assert.Equal(2, (await store.ClassAsync(lesson.ClassSessionId, default))!.HeldSeats);
            Assert.Single(await store.ClassRosterAsync(lesson.ClassSessionId, default), x => x.Status == "Pending");
        }
        Assert.True(await store.TransitionAsync(booking.Id, "coach", "manager", now, [], default));
        Assert.False(await store.TransitionAsync(booking.Id, "approve", "manager", now.AddHours(1), [], default));
        Assert.True(await store.TransitionAsync(booking.Id, "approve", "manager", now.AddMinutes(59), [], default));
        var reminders = await db.Database.GetCollection<OutboxRecord>("outbox")
            .Find(x => x.ReservationId == booking.Id && x.ProgramLessonId != null).ToListAsync();
        Assert.Equal(4, reminders.Count);
        foreach (var lesson in sessions)
        {
            Assert.Equal(2, (await store.ClassAsync(lesson.ClassSessionId, default))!.HeldSeats);
            Assert.Equal(lesson.StartUtc.AddHours(-24), reminders.Single(x => x.ProgramLessonId == lesson.ClassSessionId).DueUtc);
        }
        var date = sessions[2].ClassSessionId;
        Assert.True(await store.UpdatePackageSeatAsync(booking.Id, date, "one", "Request", now, default));
        Assert.Equal(2, (await store.ClassAsync(date, default))!.HeldSeats);
        Assert.True(await store.UpdatePackageSeatAsync(booking.Id, date, "one", "Approve", now, default));
        Assert.Equal(1, (await store.ClassAsync(date, default))!.HeldSeats);
        Assert.Equal(2, (await store.ClassAsync(sessions[3].ClassSessionId, default))!.HeldSeats);
    }
}
