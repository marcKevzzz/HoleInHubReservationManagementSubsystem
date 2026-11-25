using HoleInHub.Business.Reservations;
using HoleInHubReservationManagementSubsystem.ViewModels;
using Xunit;

namespace HoleInHub.Tests.Web.Smoke;

public sealed class ReservationFilterTests
{
    [Theory]
    [InlineData("upcoming", "Pending")]
    [InlineData("completed", "Completed")]
    [InlineData("cancelled", "Cancelled")]
    [InlineData("expired", "Expired")]
    public void StatusFiltersReturnOnlyMatchingReservations(string filter, string expected)
    {
        var model = new MyReservationsViewModel
        {
            Filter = filter, Upcoming = [Booking("Pending")],
            Past = [Booking("Completed"), Booking("Cancelled"), Booking("Expired")],
        };
        Assert.Equal(expected, Assert.Single(model.Items).Status);
        Assert.Equal(3, new MyReservationsViewModel { Filter = "past", Past = model.Past }.Items.Count);
    }

    [Theory]
    [InlineData("all")]
    [InlineData("invalid")]
    public void AllIncludesUpcomingAndPastReservations(string filter)
    {
        var model = new MyReservationsViewModel { Filter = filter, Upcoming = [Booking("Pending")], Past = [Booking("Completed"), Booking("Expired")] };
        Assert.Equal(3, model.Items.Count);
        Assert.Contains(model.Items, booking => booking.Status == "Pending");
        Assert.Contains(model.Items, booking => booking.Status == "Completed");
        Assert.Equal("all", new MyReservationsViewModel().Filter);
    }

    private static ReservationDto Booking(string status) => new(status, status, "Regular", status,
        DateTime.UtcNow, DateTime.UtcNow.AddHours(1), DateTime.UtcNow, [], "Test", "Player", "09171234567", 1, []);
}
