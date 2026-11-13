using HoleInHub.Business.Reservations;
using HoleInHub.Data.Migrations.Definitions;
using HoleInHub.Data.Reservations;
using HoleInHub.Tests.Integration.Fixtures;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Xunit;

namespace HoleInHub.Tests.Integration.Reservations;

[Trait("Category", "Integration")]
public sealed class ScheduleChangeTests(MongoDbFixture fixture) : IClassFixture<MongoDbFixture>
{
    private sealed class Clock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 11, 3, 12, 0, 0, TimeSpan.FromHours(8));
    }
    private async Task<(ReservationService Service, ReservationPersistence Store, HoleInHub.Data.Context.IMongoDbContext Context)> Setup()
    {
        Assert.True(fixture.IsServerAvailable, "Start the local MongoDB replica set.");
        var context = fixture.CreateDbContext();
        await new Migration_002_Reservations().UpAsync(context.Database, default);
        await new Migration_004_BookingProcess().UpAsync(context.Database, default);
        await new AccountsMigration().UpAsync(context.Database, default);
        await new Migration_005_GoogleAccounts().UpAsync(context.Database, default);
        await new Migration_006_SchemaHardening().UpAsync(context.Database, default);
        await new Migration_010_StaffAssistedReservations().UpAsync(context.Database, default);
        await new Migration_013_ReservationAttendance().UpAsync(context.Database, default);
        await new Migration_015_ScheduleChanges().UpAsync(context.Database, default);
        var store = new ReservationPersistence(context);
        var service = new ReservationService(store, new OutboxPersistence(context), Options.Create(new ReservationPolicy
        {
            Approved = true,
            DurationsApproved = true,
            ReviewStart = TimeSpan.FromHours(10),
            ReviewEnd = TimeSpan.FromDays(1),
            ReviewDays = [0, 1, 2, 3, 4, 5, 6],
            StaffEmails = ["staff@example.test"]
        }), new Clock());
        return (service, store, context);
    }
    private static ReservationRecord Booking(string bay = "bay-2") => new()
    {
        AccountId = "customer",
        Email = "customer@example.test",
        Reference = "HIH-" + Guid.NewGuid().ToString("N")[..10],
        FirstName = "Alex",
        LastName = "Santos",
        Phone = "09175550123",
        Attendees = 1,
        Service = "Regular",
        Status = "Confirmed",
        ResourceIds = [bay],
        StartUtc = new(2026, 11, 4, 4, 0, 0, DateTimeKind.Utc),
        EndUtc = new(2026, 11, 4, 5, 0, 0, DateTimeKind.Utc),
        CreatedUtc = new(2026, 11, 3, 3, 0, 0, DateTimeKind.Utc),
        ReviewDeadlineUtc = new(2026, 11, 3, 3, 30, 0, DateTimeKind.Utc),
        PolicyVersion = 2,
        IdempotencyKey = Guid.NewGuid().ToString("N"),
        SubmissionHash = "initial"
    };
    private static RequestScheduleChange Proposal(ReservationRecord booking) =>
        new(booking.Id, booking.Revision, Guid.NewGuid().ToString("N"), new(2026, 11, 5), new(14, 0), 90, "Move our visit.");

    [Fact]
    public async Task StaffCreationUsesOneIntervalForEveryBayAndRollsBackIfAnyBayIsBusy()
    {
        var (service, store, _) = await Setup();
        var occupied = await store.InsertAsync(Booking("bay-3"), default);
        var request = new StaffBookingRequest("bay-2", new(2026, 11, 4), new(12, 0), 60,
            "Alex", "Santos", "09175550123", 12, "Walk-in", Guid.NewGuid().ToString("N"))
        { AdditionalResourceIds = ["bay-3"] };

        await Assert.ThrowsAsync<ReservationValidationException>(() => service.CreateStaffBookingAsync(request, "staff", default));
        Assert.DoesNotContain("bay-2", await store.OccupiedAsync(occupied.StartUtc, occupied.EndUtc, default));
        await Assert.ThrowsAsync<ReservationValidationException>(() =>
            service.CreateStaffBookingAsync(request with { AdditionalResourceIds = ["vip-7"] }, "staff", default));

        var open = request with { StartTime = new(13, 0) };
        var saved = await service.CreateStaffBookingAsync(open, "staff", default);
        var retry = await service.CreateStaffBookingAsync(open with { ResourceId = "bay-3", AdditionalResourceIds = ["bay-2"] }, "staff", default);
        Assert.Equal(saved.Id, retry.Id);
        Assert.Equal(["bay-2", "bay-3"], saved.ResourceIds);
        Assert.Equal(occupied.EndUtc, saved.StartUtc);
        Assert.Equal(saved.StartUtc.AddHours(1), saved.EndUtc);
    }

    [Fact]
    public async Task BatchMoveRollsBackOnConflictOrStaleRevisionAndRetriesWithoutMovingTwice()
    {
        var (service, store, _) = await Setup();
        var first = await store.InsertAsync(Booking(), default);
        var next = Booking();
        next.StartUtc = first.EndUtc;
        next.EndUtc = next.StartUtc.AddHours(1);
        var second = await store.InsertAsync(next, default);
        var blocker = Booking();
        blocker.StartUtc = second.EndUtc.AddHours(1);
        blocker.EndUtc = blocker.StartUtc.AddHours(1);
        await store.InsertAsync(blocker, default);

        StaffBatchMoveItem[] selected = [new(first.Id, 0), new(second.Id, 0)];
        var command = Guid.NewGuid().ToString("N");
        var day = new DateOnly(2026, 11, 4);
        await Assert.ThrowsAsync<ReservationValidationException>(() =>
            service.MoveStaffBatchAsync(selected, command, day, 120, "staff", default));
        await Assert.ThrowsAsync<ReservationValidationException>(() =>
            service.MoveStaffBatchAsync([selected[0], new(second.Id, 1)], command, day, 60, "staff", default));

        foreach (var original in new[] { first, second })
        {
            var saved = (await store.ByIdAsync(original.Id, default))!;
            Assert.Equal(original.StartUtc, saved.StartUtc);
            Assert.Equal(0, saved.Revision);
            Assert.DoesNotContain(saved.History, entry => entry.CommandId == command);
        }

        await service.MoveStaffBatchAsync(selected, command, day, 60, "staff", default);
        await service.MoveStaffBatchAsync(selected.Reverse().ToArray(), command, day, 60, "staff", default);
        await Assert.ThrowsAsync<ReservationValidationException>(() =>
            service.MoveStaffBatchAsync(selected, command, day, 30, "staff", default));

        foreach (var original in new[] { first, second })
        {
            var saved = (await store.ByIdAsync(original.Id, default))!;
            Assert.Equal(original.StartUtc.AddHours(1), saved.StartUtc);
            Assert.Equal(original.EndUtc.AddHours(1), saved.EndUtc);
            Assert.Equal(original.ResourceIds, saved.ResourceIds);
            Assert.Equal(1, saved.Revision);
            Assert.Single(saved.History, entry => entry.CommandId == command);
        }
    }

    [Fact]
    public async Task VipUpgradeRequiresAgreementAndAtomicallyChangesServiceWithAnIdempotentAudit()
    {
        var (service, store, _) = await Setup();
        var original = await store.InsertAsync(Booking(), default);
        var command = Guid.NewGuid().ToString("N");
        await Assert.ThrowsAsync<ReservationValidationException>(() => service.MoveStaffBookingAsync(original.Id, 0, command,
            new(2026, 11, 4), new(12, 0), "staff", default, "vip-7", "bay-2"));
        Assert.Equal("Regular", (await store.ByIdAsync(original.Id, default))!.Service);
        await service.MoveStaffBookingAsync(original.Id, 0, command, new(2026, 11, 4), new(12, 0), "staff", default, "vip-7", "bay-2", true);
        await service.MoveStaffBookingAsync(original.Id, 0, command, new(2026, 11, 4), new(12, 0), "staff", default, "vip-7", "bay-2", true);
        var upgraded = (await store.ByIdAsync(original.Id, default))!;
        Assert.Equal("VIP", upgraded.Service);
        Assert.Equal(["vip-7"], upgraded.ResourceIds);
        Assert.Equal(original.StartUtc, upgraded.StartUtc);
        Assert.Equal(original.EndUtc, upgraded.EndUtc);
        Assert.Equal(1, upgraded.Revision);
        Assert.Contains("VIP pricing", Assert.Single(upgraded.History, entry => entry.CommandId == command).Reason);
        var occupied = await store.OccupiedAsync(original.StartUtc, original.EndUtc, default);
        Assert.Contains("vip-7", occupied);
        Assert.DoesNotContain("bay-2", occupied);
    }

    [Fact]
    public async Task ConflictingVipUpgradePreservesRegularServiceAndOriginalAllocation()
    {
        var (service, store, _) = await Setup();
        var original = await store.InsertAsync(Booking(), default);
        var vip = Booking("vip-7");
        vip.Service = "VIP";
        await store.InsertAsync(vip, default);
        await Assert.ThrowsAsync<ReservationValidationException>(() => service.MoveStaffBookingAsync(original.Id, 0,
            Guid.NewGuid().ToString("N"), new(2026, 11, 4), new(12, 0), "staff", default, "vip-7", "bay-2", true));
        var unchanged = (await store.ByIdAsync(original.Id, default))!;
        Assert.Equal("Regular", unchanged.Service);
        Assert.Equal(original.ResourceIds, unchanged.ResourceIds);
        Assert.Equal(0, unchanged.Revision);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task TimelineMoveIsAtomicIdempotentAndRejectsConflictsAndStaleUpdates(int daysAhead)
    {
        var (service, store, context) = await Setup();
        var original = await store.InsertAsync(Booking(), default);
        var targetDay = new DateOnly(2026, 11, 4).AddDays(daysAhead);
        var dayOffset = TimeSpan.FromDays(daysAhead);
        var occupied = Booking();
        occupied.StartUtc = (original.StartUtc + dayOffset).AddHours(2);
        occupied.EndUtc = occupied.StartUtc.AddHours(1);
        await store.InsertAsync(occupied, default);
        var command = Guid.NewGuid().ToString("N");
        await Assert.ThrowsAsync<ReservationValidationException>(() => service.MoveStaffBookingAsync(original.Id, 0,
            command, targetDay, new(14, 0), "staff", default));
        var unchanged = (await store.ByIdAsync(original.Id, default))!;
        Assert.Equal(original.StartUtc, unchanged.StartUtc);
        Assert.Null(unchanged.ScheduleChange);
        Assert.Equal(0, unchanged.Revision);
        await service.MoveStaffBookingAsync(original.Id, 0, command, targetDay, new(15, 0), "staff", default);
        await service.MoveStaffBookingAsync(original.Id, 0, command, targetDay, new(15, 0), "staff", default);
        var moved = (await store.ByIdAsync(original.Id, default))!;
        Assert.Equal((original.StartUtc + dayOffset).AddHours(3), moved.StartUtc);
        Assert.Equal((original.EndUtc + dayOffset).AddHours(3), moved.EndUtc);
        Assert.Equal(1, moved.Revision);
        Assert.Equal("Approved", moved.ScheduleChange!.Status);
        Assert.Single(moved.History, entry => entry.CommandId == command);
        await Assert.ThrowsAsync<ReservationValidationException>(() => service.MoveStaffBookingAsync(original.Id, 0,
            command, targetDay, new(16, 0), "staff", default));
        await Assert.ThrowsAsync<ReservationValidationException>(() => service.MoveStaffBookingAsync(original.Id, 0,
            Guid.NewGuid().ToString("N"), targetDay, new(16, 0), "staff", default));
        Assert.Equal(1, await context.Database.GetCollection<OutboxRecord>("outbox").CountDocumentsAsync(x => x.Subject == "Schedule change approved"));
    }

    [Fact]
    public async Task ExpiredRequestsCanBeBrowsedByManilaSessionDateWithoutEnteringTheActiveQueue()
    {
        var (service, store, _) = await Setup();
        var expired = Booking();
        expired.Status = "Expired";
        var original = await store.InsertAsync(expired, default);
        var next = Booking();
        next.Status = "Expired";
        next.StartUtc = next.StartUtc.AddDays(1);
        next.EndUtc = next.EndUtc.AddDays(1);
        next.ReviewDeadlineUtc = next.ReviewDeadlineUtc.AddHours(1);
        var later = await store.InsertAsync(next, default);
        var cancelled = Booking();
        cancelled.Status = "Cancelled";
        await store.InsertAsync(cancelled, default);
        Assert.Equal(new[] { later.Id, original.Id }, (await service.GetExpiredRequestsAsync(null, default)).Select(item => item.Id));
        Assert.Equal(original.Id, Assert.Single(await service.GetExpiredRequestsAsync(new(2026, 11, 4), default)).Id);
        Assert.Empty(await service.GetReviewQueueAsync(default));
        Assert.Empty(await store.OccupiedAsync(original.StartUtc, original.EndUtc, default));
    }

    [Fact]
    public async Task StaffCanDragAnUpcomingConfirmedBookingWithLessThanOneHourNotice()
    {
        var (service, store, _) = await Setup();
        var booking = Booking();
        booking.StartUtc = new(2026, 11, 3, 4, 30, 0, DateTimeKind.Utc);
        booking.EndUtc = booking.StartUtc.AddHours(1);
        var original = await store.InsertAsync(booking, default);
        var schedule = await service.GetStaffScheduleAsync(new(2026, 11, 3), default);
        var entry = Assert.Single(schedule.SelectMany(bay => bay.Entries), entry => entry.ReservationId == original.Id);
        Assert.True(entry.CanMove);
        await service.MoveStaffBookingAsync(original.Id, 0, Guid.NewGuid().ToString("N"), new(2026, 11, 3),
            new(13, 0), "staff", default, "bay-3", "bay-2");
        var moved = (await store.ByIdAsync(original.Id, default))!;
        Assert.Equal(["bay-3"], moved.ResourceIds);
        Assert.Equal(new DateTime(2026, 11, 3, 5, 0, 0, DateTimeKind.Utc), moved.StartUtc);
    }

    [Fact]
    public async Task TimelineAllowsAnotherBookingInTheDestinationBayOutsideTheMovedInterval()
    {
        var (service, store, _) = await Setup();
        var original = await store.InsertAsync(Booking(), default);
        var earlier = Booking("bay-3");
        earlier.StartUtc = original.StartUtc.AddHours(-1);
        earlier.EndUtc = original.StartUtc;
        await store.InsertAsync(earlier, default);
        var later = Booking("bay-3");
        later.StartUtc = original.EndUtc;
        later.EndUtc = original.EndUtc.AddHours(1);
        await store.InsertAsync(later, default);
        await service.MoveStaffBookingAsync(original.Id, 0, Guid.NewGuid().ToString("N"), new(2026, 11, 4),
            new(12, 0), "staff", default, "bay-3", "bay-2");
        var moved = (await store.ByIdAsync(original.Id, default))!;
        Assert.Equal(["bay-3"], moved.ResourceIds);
        Assert.Equal(original.StartUtc, moved.StartUtc);
        Assert.Equal(original.EndUtc, moved.EndUtc);
    }

    [Fact]
    public async Task TimelineCanChangeBayWithoutChangingTimeAndPreservesOtherBaysAndParticipants()
    {
        var (service, store, _) = await Setup();
        var booking = Booking();
        booking.ResourceIds = ["bay-2", "bay-3"];
        booking.Participants = [new() { ResourceId = "bay-2", Players = 1 }, new() { ResourceId = "bay-3", Players = 1 }];
        booking.Attendees = 2;
        var original = await store.InsertAsync(booking, default);
        var command = Guid.NewGuid().ToString("N");
        await service.MoveStaffBookingAsync(original.Id, 0, command, new(2026, 11, 4), new(12, 0), "staff", default, "bay-4", "bay-2");
        await service.MoveStaffBookingAsync(original.Id, 0, command, new(2026, 11, 4), new(12, 0), "staff", default, "bay-4", "bay-2");
        var moved = (await store.ByIdAsync(original.Id, default))!;
        Assert.Equal(["bay-4", "bay-3"], moved.ResourceIds);
        Assert.Equal(["bay-4", "bay-3"], moved.Participants.Select(participant => participant.ResourceId));
        Assert.Equal(original.StartUtc, moved.StartUtc);
        Assert.Equal(original.EndUtc, moved.EndUtc);
        Assert.Equal(1, moved.Revision);
        await Assert.ThrowsAsync<ReservationValidationException>(() => service.MoveStaffBookingAsync(original.Id, 0,
            command, new(2026, 11, 4), new(12, 0), "staff", default, "bay-5", "bay-2"));
    }

    [Theory]
    [InlineData("bay-3", "bay-2")]
    [InlineData("vip-7", "bay-2")]
    [InlineData("missing", "bay-2")]
    [InlineData("bay-4", "missing")]
    [InlineData("bay-5", "bay-2")]
    public async Task TimelineRejectsOccupiedIncompatibleMissingOrAlreadySelectedBays(string target, string source)
    {
        var (service, store, _) = await Setup();
        var booking = Booking();
        booking.ResourceIds = ["bay-2", "bay-5"];
        var original = await store.InsertAsync(booking, default);
        await store.InsertAsync(Booking("bay-3"), default);
        await Assert.ThrowsAsync<ReservationValidationException>(() => service.MoveStaffBookingAsync(original.Id, 0,
            Guid.NewGuid().ToString("N"), new(2026, 11, 4), new(12, 0), "staff", default, target, source));
        var unchanged = (await store.ByIdAsync(original.Id, default))!;
        Assert.Equal(original.ResourceIds, unchanged.ResourceIds);
        Assert.Equal(original.StartUtc, unchanged.StartUtc);
        Assert.Equal(0, unchanged.Revision);
    }

    [Fact]
    public async Task TimelineMovesContendOnTheSameAllocationGuards()
    {
        var (service, store, _) = await Setup();
        var first = await store.InsertAsync(Booking(), default);
        var later = Booking("bay-3");
        later.StartUtc = later.StartUtc.AddHours(2);
        later.EndUtc = later.EndUtc.AddHours(2);
        var second = await store.InsertAsync(later, default);
        async Task<bool> Move(ReservationRecord booking)
        {
            try { await service.MoveStaffBookingAsync(booking.Id, 0, Guid.NewGuid().ToString("N"), new(2026, 11, 4), new(16, 0), "staff", default, "bay-4", booking.ResourceIds[0]); return true; }
            catch (ReservationValidationException) { return false; }
        }
        Assert.Single(await Task.WhenAll(Move(first), Move(second)), success => success);
    }

    [Fact]
    public async Task ProposalHoldsOnlyOriginalAndApprovalAtomicallyMovesItWithReplayProtection()
    {
        var (service, store, context) = await Setup();
        var original = await store.InsertAsync(Booking(), default);
        var request = Proposal(original);
        await service.RequestScheduleChangeAsync(request, "Customer", default);
        await service.RequestScheduleChangeAsync(request, "Customer", default);
        await Assert.ThrowsAsync<ReservationValidationException>(() =>
            service.RequestScheduleChangeAsync(request with { Reason = "A different request." }, "Customer", default));
        var pending = (await store.ByIdAsync(original.Id, default))!;
        Assert.Equal(original.StartUtc, pending.StartUtc);
        Assert.Equal(1, pending.Revision);
        Assert.Contains("bay-2", await store.OccupiedAsync(original.StartUtc, original.EndUtc, default));
        var start = ReservationRules.Interval(request.Date, request.StartTime, request.DurationMinutes).Start;
        Assert.Empty(await store.OccupiedAsync(start, start.AddMinutes(90), default));
        await service.DecideScheduleChangeAsync(original.Id, request.CommandId, pending.Revision, true, ["bay-3"], "staff", null, default);
        var moved = (await store.ByIdAsync(original.Id, default))!;
        Assert.Equal(start, moved.StartUtc);
        Assert.Equal(["bay-3"], moved.ResourceIds);
        Assert.Equal("Approved", moved.ScheduleChange!.Status);
        Assert.Empty(await store.OccupiedAsync(original.StartUtc, original.EndUtc, default));
        Assert.Contains("bay-3", await store.OccupiedAsync(start, start.AddMinutes(90), default));
        await service.RequestScheduleChangeAsync(request, "Customer", default);
        await Assert.ThrowsAsync<ReservationValidationException>(() =>
            service.DecideScheduleChangeAsync(original.Id, request.CommandId, pending.Revision, true, ["bay-3"], "staff", null, default));
        Assert.Equal(2, (await store.ByIdAsync(original.Id, default))!.Revision);
        Assert.Equal(1, await context.Database.GetCollection<OutboxRecord>("outbox").CountDocumentsAsync(x => x.Subject == "Schedule change approved"));
    }

    [Fact]
    public async Task ConflictingOrStaleApprovalPreservesOriginalAndDeclineReleasesNothing()
    {
        var (service, store, _) = await Setup();
        var original = await store.InsertAsync(Booking(), default);
        var request = Proposal(original);
        await service.RequestScheduleChangeAsync(request, "Customer", default);
        var occupied = Booking("bay-3");
        occupied.StartUtc = ReservationRules.Interval(request.Date, request.StartTime, request.DurationMinutes).Start;
        occupied.EndUtc = occupied.StartUtc.AddMinutes(90);
        await store.InsertAsync(occupied, default);
        await Assert.ThrowsAsync<ReservationValidationException>(() =>
            service.DecideScheduleChangeAsync(original.Id, request.CommandId, 0, true, ["bay-2"], "staff", null, default));
        await Assert.ThrowsAsync<ReservationValidationException>(() =>
            service.DecideScheduleChangeAsync(original.Id, request.CommandId, 1, true, ["bay-3"], "staff", null, default));
        var unchanged = (await store.ByIdAsync(original.Id, default))!;
        Assert.Equal(original.StartUtc, unchanged.StartUtc);
        Assert.Equal(["bay-2"], unchanged.ResourceIds);
        Assert.Equal("Pending", unchanged.ScheduleChange!.Status);
        Assert.Equal(1, unchanged.Revision);
        await service.DecideScheduleChangeAsync(original.Id, request.CommandId, 1, false, ["bay-2"], "staff", "Bay unavailable.", default);
        Assert.Equal(original.StartUtc, (await store.ByIdAsync(original.Id, default))!.StartUtc);
        Assert.Contains("bay-2", await store.OccupiedAsync(original.StartUtc, original.EndUtc, default));
    }

    [Fact]
    public async Task ConcurrentMovesToSameBayHaveOneWinner()
    {
        var (service, store, _) = await Setup();
        var first = await store.InsertAsync(Booking("bay-2"), default);
        var second = await store.InsertAsync(Booking("bay-3"), default);
        var requests = new[] { Proposal(first), Proposal(second) };
        foreach (var request in requests) { await service.RequestScheduleChangeAsync(request, "Customer", default); }
        async Task<bool> Approve(RequestScheduleChange request)
        {
            try { await service.DecideScheduleChangeAsync(request.BookingId, request.CommandId, 1, true, ["bay-4"], "staff", null, default); return true; }
            catch (ReservationValidationException) { return false; }
        }
        Assert.Single(await Task.WhenAll(requests.Select(Approve)), result => result);
    }
}
