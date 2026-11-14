using HoleInHub.Business.Reservations;
using HoleInHub.Data.Reservations;
using Xunit;

namespace HoleInHub.Tests.Unit.Reservations;

public sealed class ReservationReminderTests
{
    private static readonly DateTime Now = new(2026, 11, 14, 15, 50, 0, DateTimeKind.Utc);
    private static ReservationRecord Booking() => new()
    {
        Id = "booking", Reference = "HIH-TEST", Status = "Confirmed", Email = "customer@example.test",
        StartUtc = Now.AddMinutes(15), EndUtc = Now.AddMinutes(75)
    };

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(900, true)]
    [InlineData(901, false)]
    [InlineData(86400, false)]
    public void OnlyFutureSessionsWithinFifteenMinutesGetReminders(int secondsUntilStart, bool eligible)
    {
        var booking = Booking();
        booking.StartUtc = Now.AddSeconds(secondsUntilStart);
        Assert.Equal(eligible, ReservationReminderPolicy.Create(booking, Now) is not null);
    }

    [Theory]
    [InlineData("Pending", "customer@example.test", false, false)]
    [InlineData("Cancelled", "customer@example.test", false, false)]
    [InlineData("Checked-In", "customer@example.test", false, false)]
    [InlineData("Confirmed", "", false, false)]
    [InlineData("Confirmed", "invalid email", false, false)]
    [InlineData("Confirmed", "Name <customer@example.test>", false, false)]
    [InlineData("Confirmed", "customer@example.test", true, false)]
    [InlineData("Confirmed", "customer@example.test", false, true)]
    public void UnconfirmedOrUncontactableSessionsDoNotGetReminders(string status, string email, bool checkedIn, bool cancelling)
    {
        var booking = Booking();
        booking.Status = status;
        booking.Email = email;
        booking.CheckedInUtc = checkedIn ? Now : null;
        booking.CancellationRequested = cancelling;
        Assert.Null(ReservationReminderPolicy.Create(booking, Now));
    }

    [Fact]
    public void ReminderHasStableScheduleIdentityAndManilaDateAcrossMidnight()
    {
        var booking = Booking();
        var message = ReservationReminderPolicy.Create(booking, Now)!;
        Assert.Equal(ReservationReminderPolicy.Create(booking, Now.AddMinutes(10))!.Id, message.Id);
        Assert.Equal("booking:reminder:" + booking.StartUtc.Ticks, message.Id);
        Assert.Contains("November 15, 2026", message.Body);
        Assert.Contains("12:05 AM", message.Body);
        Assert.Contains("Manila", message.Body);
        Assert.DoesNotContain("http", message.Body);
        Assert.Equal("Confirmed", message.RequiredStatus);
        Assert.Equal(booking.StartUtc, message.ReminderStartUtc);
        Assert.Equal(Now, message.DueUtc);
        booking.StartUtc = booking.StartUtc.AddMinutes(-1);
        Assert.NotEqual(message.Id, ReservationReminderPolicy.Create(booking, Now)!.Id);
    }
}
