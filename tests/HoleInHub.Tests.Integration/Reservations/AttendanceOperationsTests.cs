using HoleInHub.Business.Reservations;
using HoleInHub.Data.Migrations.Definitions;
using HoleInHub.Data.Reservations;
using MongoDB.Driver;
using Xunit;

namespace HoleInHub.Tests.Integration.Reservations;

public sealed partial class AttendanceTests
{
    [Fact]
    public async Task AttendanceCommandsSavedBeforeThisUpgradeRemainReplayable()
    {
        var (store, context) = await Setup();
        var booking = Booking("Checked-In"); booking.CheckedInUtc = Start; booking.Revision = 1;
        var command = Command(booking.Id);
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new
        { command.ReservationId, command.Action, command.ExpectedRevision, Actor = "staff", Reason = "" })));
        booking.History.Add(new()
        {
            EventType = "ReservationCheckedIn",
            Status = "Checked-In",
            ReservationStatus = "Checked-In",
            AtUtc = Start,
            Actor = "staff",
            CommandId = command.CommandId,
            CommandHash = hash
        });
        await store.InsertAsync(booking, default);
        Assert.Equal(AttendanceResult.AlreadyApplied, await Service(store, context, new Clock()).RecordAttendanceAsync(command, "staff", default));
        Assert.Single((await store.ByIdAsync(booking.Id, default))!.History);
    }

    [Fact]
    public async Task NoShowReleasesCapacityAndRetriesKeepOneAuditAndNotification()
    {
        var (store, context) = await Setup();
        await new Migration_020_AttendanceOperations().UpAsync(context.Database, default);
        var clock = new Clock { Now = Start.AddHours(1) };
        var service = Service(store, context, clock);
        var booking = await store.InsertAsync(Booking(), default);
        var command = Command(booking.Id, "no-show", reason: "Called guest; not attending");
        Assert.Equal(AttendanceResult.Applied, await service.RecordAttendanceAsync(command, "staff", default));
        Assert.Equal(AttendanceResult.AlreadyApplied, await service.RecordAttendanceAsync(command, "staff", default));
        var saved = (await store.ByIdAsync(booking.Id, default))!;
        Assert.Equal("No-show", saved.Status);
        Assert.Equal("Confirmed", Assert.Single(saved.History).AttendanceBefore!.Status);
        Assert.Equal("No-show", saved.History[0].AttendanceAfter!.Status);
        Assert.DoesNotContain("bay-2", await store.OccupiedAsync(clock.Now, booking.EndUtc, default));
        Assert.Single(await context.Database.GetCollection<OutboxRecord>("outbox").Find(x => x.ReservationId == booking.Id && x.Subject == "Reservation marked no-show").ToListAsync());
        Assert.Contains((await service.GetStaffScheduleAsync(new DateOnly(2026, 11, 10), default)).SelectMany(bay => bay.Entries), row => row.ReservationId == booking.Id && row.Status == "No-show");
    }

    [Fact]
    public async Task CorrectionRejectsReallocatedBaysThenRestoresAndClearsDepartureAtomically()
    {
        var (store, context) = await Setup();
        var clock = new Clock { Now = Start.AddMinutes(45) };
        var service = Service(store, context, clock);
        var original = Booking("Completed"); original.CheckedInUtc = Start; original.CheckedOutUtc = Start.AddMinutes(30);
        var booking = await store.InsertAsync(original, default);
        var other = Booking(); other.StartUtc = Start.AddMinutes(30);
        var blocker = await store.InsertAsync(other, default);
        var command = new AttendanceCommand(booking.Id, "correct", 0, Guid.NewGuid().ToString("N"), "Checkout recorded by mistake", "Checked-In", Start);
        Assert.Equal(AttendanceResult.NotEligible, await service.RecordAttendanceAsync(command, "staff", default));
        Assert.Equal("Completed", (await store.ByIdAsync(booking.Id, default))!.Status);
        Assert.Empty((await store.ByIdAsync(booking.Id, default))!.History);
        await context.Database.GetCollection<ReservationRecord>("reservations").DeleteOneAsync(x => x.Id == blocker.Id);
        Assert.Equal(AttendanceResult.Applied, await service.RecordAttendanceAsync(command, "staff", default));
        Assert.Equal(AttendanceResult.AlreadyApplied, await service.RecordAttendanceAsync(command, "staff", default));
        var saved = (await store.ByIdAsync(booking.Id, default))!;
        Assert.Equal("Checked-In", saved.Status);
        Assert.Null(saved.CheckedOutUtc);
        Assert.Equal(original.CheckedOutUtc, Assert.Single(saved.History).AttendanceBefore!.CheckedOutUtc);
        Assert.Null(saved.History[0].AttendanceAfter!.CheckedOutUtc);
        Assert.Equal(original.AccountId, saved.AccountId);
    }

    [Fact]
    public async Task MultiBayExtensionRejectsOneConflictingBayAndCommitsOneNotificationAfterRetry()
    {
        var (store, context) = await Setup();
        var clock = new Clock { Now = Start.AddHours(1) };
        var service = Service(store, context, clock);
        var booking = await store.InsertAsync(Booking(resources: ["bay-2", "bay-3"]), default);
        var next = Booking(resources: ["bay-3"]); next.StartUtc = booking.EndUtc; next.EndUtc = booking.EndUtc.AddHours(1);
        var blocker = await store.InsertAsync(next, default);
        var command = new AttendanceCommand(booking.Id, "extend", 0, Guid.NewGuid().ToString("N"), "Customer agreed", ExtensionMinutes: 30);
        Assert.Equal(AttendanceResult.NotEligible, await service.RecordAttendanceAsync(command, "staff", default));
        Assert.Equal(booking.EndUtc, (await store.ByIdAsync(booking.Id, default))!.EndUtc);
        Assert.Empty((await store.ByIdAsync(booking.Id, default))!.History);
        await context.Database.GetCollection<ReservationRecord>("reservations").DeleteOneAsync(x => x.Id == blocker.Id);
        Assert.Equal(AttendanceResult.Applied, await service.RecordAttendanceAsync(command, "staff", default));
        Assert.Equal(AttendanceResult.AlreadyApplied, await service.RecordAttendanceAsync(command, "staff", default));
        var saved = (await store.ByIdAsync(booking.Id, default))!;
        Assert.Equal(booking.EndUtc.AddMinutes(30), saved.EndUtc);
        Assert.Equal(booking.ResourceIds, saved.ResourceIds);
        Assert.Equal(booking.EndUtc, Assert.Single(saved.History).AttendanceBefore!.EndUtc);
        Assert.Single(await context.Database.GetCollection<OutboxRecord>("outbox").Find(x => x.ReservationId == booking.Id && x.Subject == "Reservation extended").ToListAsync());
    }

    [Fact]
    public async Task ConcurrentExtensionsFromTheSameRevisionHaveOneWinner()
    {
        var (store, context) = await Setup();
        var service = Service(store, context, new Clock { Now = Start.AddHours(1) });
        var booking = await store.InsertAsync(Booking(), default);
        AttendanceCommand Command() => new(booking.Id, "extend", 0, Guid.NewGuid().ToString("N"), "Agreed", ExtensionMinutes: 30);
        var results = await Task.WhenAll(service.RecordAttendanceAsync(Command(), "staff-a", default), service.RecordAttendanceAsync(Command(), "staff-b", default));
        Assert.Single(results, result => result == AttendanceResult.Applied);
        Assert.Single(results, result => result == AttendanceResult.Stale);
        Assert.Equal(booking.EndUtc.AddMinutes(30), (await store.ByIdAsync(booking.Id, default))!.EndUtc);
    }

    [Fact]
    public async Task ExtensionRejectsMaintenanceInTheAdditionalInterval()
    {
        var (store, context) = await Setup();
        var service = Service(store, context, new Clock { Now = Start.AddHours(1) });
        var booking = await store.InsertAsync(Booking(), default);
        await context.Database.GetCollection<ScheduleBlock>("schedule_blocks").InsertOneAsync(new()
        { Id = "maintenance", ResourceIds = ["bay-2"], StartUtc = booking.EndUtc, EndUtc = booking.EndUtc.AddHours(1) });
        var command = new AttendanceCommand(booking.Id, "extend", 0, Guid.NewGuid().ToString("N"), "Agreed", ExtensionMinutes: 30);
        Assert.Equal(AttendanceResult.NotEligible, await service.RecordAttendanceAsync(command, "staff", default));
        Assert.Empty((await store.ByIdAsync(booking.Id, default))!.History);
    }
}
