using HoleInHubReservationManagementSubsystem.ViewModels;
using Xunit;

namespace HoleInHub.Tests.Web.Smoke;

public sealed class StaffNotificationTextTests
{
    [Theory]
    [InlineData("Pending request: PRIVATE-REF", "Reservation needs review")]
    [InlineData("Reservation request received", "Reservation needs review")]
    [InlineData("Upcoming reservation: PRIVATE-REF", "Upcoming reservation")]
    [InlineData("Cancellation review needed", "Cancellation request needs review")]
    [InlineData("Reservation cancelled", "Reservation cancelled")]
    public void NoticesDescribeTheTaskWithoutEmailOrReferenceDetails(string subject, string title) =>
        Assert.Equal(title, StaffNotificationText.Title(subject));
}
