using HoleInHub.Business.Reservations;
using HoleInHub.Data.Reservations;
using Xunit;

namespace HoleInHub.Tests.Unit.Reservations;

public sealed class AttendanceOperationsTests
{
    private static readonly DateTime Start = new(2026, 11, 15, 6, 0, 0, DateTimeKind.Utc);
    private static ReservationRecord Booking() => new()
    { Status = "Confirmed", Service = "Regular", StartUtc = Start, EndUtc = Start.AddHours(2) };

    [Fact]
    public void NoShowOpensAtGraceDeadlineAndRequiresReasonAndNoArrival()
    {
        var booking = Booking();
        Assert.Null(AttendanceRules.Decide(booking, "no-show", "Contacted guest", Start.AddHours(1).AddTicks(-1)));
        Assert.Throws<ReservationValidationException>(() => AttendanceRules.Decide(booking, "no-show", "", Start.AddHours(1)));
        var change = AttendanceRules.Decide(booking, "no-show", "Contacted guest", Start.AddHours(1))!;
        Assert.Equal("No-show", change.Status);
        Assert.False(change.CheckAllocation);
        booking.CheckedInUtc = Start;
        Assert.Null(AttendanceRules.Decide(booking, "no-show", "Contacted guest", Start.AddHours(1)));
        booking.CheckedInUtc = null;
        booking.CheckedOutUtc = Start;
        Assert.False(AttendanceRules.CanMarkNoShow(booking, Start.AddHours(1)));
        booking.CheckedOutUtc = null;
        booking.CancellationRequested = true;
        Assert.False(AttendanceRules.CanMarkNoShow(booking, Start.AddHours(1)));
    }

    [Fact]
    public void ShortSessionsUseTheirEndAsNoShowDeadline()
    {
        var booking = Booking(); booking.EndUtc = Start.AddMinutes(30);
        Assert.False(AttendanceRules.CanMarkNoShow(booking, Start.AddMinutes(30).AddTicks(-1)));
        Assert.True(AttendanceRules.CanMarkNoShow(booking, Start.AddMinutes(30)));
    }

    [Fact]
    public void CorrectionRestoresAllocationAndClearsActualTimesWithoutChangingSchedule()
    {
        var booking = Booking(); booking.Status = "Completed"; booking.CheckedInUtc = Start; booking.CheckedOutUtc = Start.AddMinutes(30);
        var change = AttendanceCorrectionRules.Decide(booking, "Confirmed", null, null, "Accidental checkout", Start.AddMinutes(40))!;
        Assert.True(change.CheckAllocation);
        Assert.True(change.ReplaceActualTimes);
        Assert.Null(change.CheckedInUtc);
        Assert.Null(change.CheckedOutUtc);
        Assert.Equal(Start.AddHours(2), booking.EndUtc);
    }

    [Theory]
    [InlineData("Completed", null, null)]
    [InlineData("Checked-In", 10, 20)]
    [InlineData("Completed", 20, 10)]
    [InlineData("Completed", -61, 20)]
    [InlineData("Completed", 10, 80)]
    [InlineData("Confirmed", 10, null)]
    [InlineData("Cancelled", null, null)]
    public void CorrectionRejectsInvalidStatusOrAttendanceTimes(string status, int? arrival, int? departure)
    {
        Assert.Throws<ReservationValidationException>(() => AttendanceCorrectionRules.Decide(Booking(), status,
            arrival.HasValue ? Start.AddMinutes(arrival.Value) : null,
            departure.HasValue ? Start.AddMinutes(departure.Value) : null, "Correction", Start.AddMinutes(60)));
    }

    [Fact]
    public void CorrectionRequiresReasonAndPreservesCoachAndDepositPrerequisites()
    {
        Assert.Throws<ReservationValidationException>(() => AttendanceCorrectionRules.Decide(Booking(), "Checked-In", Start, null, "", Start));
        var booking = Booking(); booking.Status = "No-show"; booking.Service = "WholeShop";
        Assert.Throws<ReservationValidationException>(() => AttendanceCorrectionRules.Decide(booking, "Confirmed", null, null, "Review", Start));
        booking.Service = "KpgaPro";
        Assert.Throws<ReservationValidationException>(() => AttendanceCorrectionRules.Decide(booking, "Confirmed", null, null, "Review", Start));
        booking.Status = "Cancelled";
        Assert.Null(AttendanceCorrectionRules.Decide(booking, "Confirmed", null, null, "Review", Start));
    }

    [Fact]
    public void ExtensionKeepsStartStatusAndActualAttendance()
    {
        var booking = Booking(); booking.Status = "Checked-In"; booking.CheckedInUtc = Start;
        var change = SessionExtensionRules.Decide(booking, 30, "Customer agreed", Start.AddHours(1))!;
        Assert.Equal(Start.AddMinutes(150), change.EndUtc);
        Assert.True(change.CheckAllocation);
        Assert.False(change.ReplaceActualTimes);
        Assert.Equal("Checked-In", change.Status);
        Assert.Equal(Start, booking.StartUtc);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(15)]
    [InlineData(45)]
    [InlineData(870)]
    public void ExtensionRequiresHalfHourIncrements(int minutes) =>
        Assert.Throws<ReservationValidationException>(() => SessionExtensionRules.Decide(Booking(), minutes, "Agreed", Start));

    [Fact]
    public void ExtensionHonoursClosingTimeAndCurrentReservationState()
    {
        var booking = Booking(); booking.StartUtc = Start.Date.AddHours(14); booking.EndUtc = Start.Date.AddHours(15);
        Assert.Equal(Start.Date.AddHours(16), SessionExtensionRules.Decide(booking, 60, "Agreed", booking.StartUtc)!.EndUtc);
        Assert.Throws<ReservationValidationException>(() => SessionExtensionRules.Decide(booking, 90, "Agreed", booking.StartUtc));
        Assert.Null(SessionExtensionRules.Decide(booking, 30, "Agreed", booking.EndUtc));
        Assert.Null(SessionExtensionRules.Decide(booking, 30, "Agreed", booking.StartUtc.AddTicks(-1)));
        Assert.False(SessionExtensionRules.CanExtend("Confirmed", "Regular", booking.StartUtc, Start.Date.AddHours(16), booking.StartUtc));
        booking.Service = "WholeShop";
        Assert.Null(SessionExtensionRules.Decide(booking, 30, "Agreed", booking.StartUtc));
        booking.Service = "VIP"; booking.CancellationRequested = true;
        Assert.Null(SessionExtensionRules.Decide(booking, 30, "Agreed", booking.StartUtc));
    }
}
