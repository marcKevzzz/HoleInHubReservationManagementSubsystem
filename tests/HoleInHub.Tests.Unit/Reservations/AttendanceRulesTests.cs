using HoleInHub.Business.Reservations;
using HoleInHub.Data.Reservations;
using Xunit;

namespace HoleInHub.Tests.Unit.Reservations;

public sealed class AttendanceRulesTests
{
    private static readonly DateTime Start = new(2026, 11, 10, 5, 0, 0, DateTimeKind.Utc);
    private static ReservationRecord Booking(string status = "Confirmed") => new()
    { Status = status, StartUtc = Start, EndUtc = Start.AddHours(2), Service = "Regular" };

    [Fact]
    public void CheckInUsesHalfOpenWindowAndDoesNotStopAtGraceDeadline()
    {
        Assert.Null(AttendanceRules.Decide(Booking(), "check-in", "", Start.AddTicks(-1)));
        Assert.Equal("Checked-In", AttendanceRules.Decide(Booking(), "check-in", "", Start)!.Status);
        Assert.NotNull(AttendanceRules.Decide(Booking(), "check-in", "", Start.AddMinutes(90)));
        Assert.Null(AttendanceRules.Decide(Booking(), "check-in", "", Start.AddHours(2)));
    }

    [Theory]
    [InlineData("Pending")]
    [InlineData("Cancelled")]
    [InlineData("Checked-In")]
    [InlineData("Completed")]
    [InlineData("No-show")]
    public void OnlyConfirmedBookingsCanCheckIn(string status) => Assert.Null(AttendanceRules.Decide(Booking(status), "check-in", "", Start));

    [Fact]
    public void CancellationCoachAndDepositPrerequisitesArePreserved()
    {
        var booking = Booking(); booking.CancellationRequested = true;
        Assert.Null(AttendanceRules.Decide(booking, "check-in", "", Start));
        booking.CancellationRequested = false; booking.Service = "KpgaPro";
        Assert.Null(AttendanceRules.Decide(booking, "check-in", "", Start));
        booking.CoachConfirmed = true;
        Assert.NotNull(AttendanceRules.Decide(booking, "check-in", "", Start));
        booking.Service = "WholeShop";
        Assert.Null(AttendanceRules.Decide(booking, "check-in", "", Start));
        booking.PaymentStatus = "DepositReceived";
        Assert.NotNull(AttendanceRules.Decide(booking, "check-in", "", Start));
    }

    [Fact]
    public void EarlyCheckoutRequiresReasonAndPreservesScheduledInterval()
    {
        var booking = Booking("Checked-In");
        Assert.Throws<ReservationValidationException>(() => AttendanceRules.Decide(booking, "check-out", "", Start.AddMinutes(30)));
        var change = AttendanceRules.Decide(booking, "check-out", "Finished early", Start.AddMinutes(30))!;
        Assert.Equal("Completed", change.Status);
        Assert.Equal(Start.AddMinutes(30), change.CheckedOutUtc);
        Assert.Null(change.CheckedInUtc);
        Assert.Equal(Start.AddHours(2), booking.EndUtc);
        Assert.NotNull(AttendanceRules.Decide(booking, "check-out", "", Start.AddHours(3)));
        Assert.Null(AttendanceRules.Decide(Booking(), "check-out", "", Start.AddHours(3)));
    }

    [Fact]
    public void CheckoutCannotPrecedeRecordedArrival()
    {
        var booking = Booking("Checked-In"); booking.CheckedInUtc = Start.AddMinutes(20);
        Assert.Null(AttendanceRules.Decide(booking, "check-out", "Finished", Start.AddMinutes(10)));
    }

    [Fact]
    public void FollowUpDeadlineIsCappedAtScheduledEnd()
    {
        Assert.Equal(Start.AddHours(1), AttendanceRules.FollowUpDue(Start, Start.AddHours(2)));
        Assert.Equal(Start.AddMinutes(30), AttendanceRules.FollowUpDue(Start, Start.AddMinutes(30)));
    }
}
