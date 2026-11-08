using HoleInHub.Business.Reservations;
using Xunit;

namespace HoleInHub.Tests.Unit.Reservations;

public sealed class ScheduleChangeRulesTests
{
    [Theory]
    [InlineData("Confirmed", 1, true)]
    [InlineData("Confirmed", 59, true)]
    [InlineData("Confirmed", 0, false)]
    [InlineData("Confirmed", -1, false)]
    [InlineData("Checked-In", 30, false)]
    [InlineData("Pending", 30, false)]
    public void StaffCanMoveFutureConfirmedBookingsWithoutCustomerNoticeRestriction(string status, int lead, bool expected)
    {
        var now = new DateTime(2026, 11, 3, 4, 0, 0, DateTimeKind.Utc);
        var booking = new ReservationDto("id", "ref", "Regular", status, now.AddMinutes(lead), now.AddMinutes(lead + 60),
            now, ["bay-2"], "Alex", "Santos", "09175550123", 1, []);
        Assert.Equal(expected, ReservationService.CanMoveStaffBooking(booking, now));
        Assert.False(ReservationService.CanMoveStaffBooking(booking with { CancellationRequested = true }, now));
    }

    [Theory]
    [InlineData("Confirmed", "Regular", 60, true)]
    [InlineData("Confirmed", "VIP", 60, true)]
    [InlineData("Confirmed", "Regular", 59, false)]
    [InlineData("Pending", "Regular", 120, false)]
    [InlineData("Checked-In", "Regular", 120, false)]
    [InlineData("Completed", "Regular", 120, false)]
    [InlineData("Confirmed", "WholeShop", 120, false)]
    [InlineData("Confirmed", "KpgaPro", 120, false)]
    public void CustomerChangesRequireConfirmedRegularOrVipWithOneHourNotice(string status, string service, int lead, bool expected)
    {
        var now = new DateTime(2026, 11, 3, 4, 0, 0, DateTimeKind.Utc);
        var booking = new ReservationDto("id", "ref", service, status, now.AddMinutes(lead), now.AddMinutes(lead + 60),
            now, ["bay-2"], "Alex", "Santos", "09175550123", 1, []);
        Assert.Equal(expected, ReservationService.CanRequestScheduleChange(booking, now));
        Assert.False(ReservationService.CanRequestScheduleChange(booking with { CancellationRequested = true }, now));
    }
}
