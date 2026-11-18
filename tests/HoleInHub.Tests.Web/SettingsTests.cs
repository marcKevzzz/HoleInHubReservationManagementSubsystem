using HoleInHub.Business.Accounts;
using HoleInHubReservationManagementSubsystem.Controllers;
using HoleInHubReservationManagementSubsystem.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Xunit;
namespace HoleInHub.Tests.Web;

public sealed class SettingsTests
{
    [Fact]
    public void SettingsWriteRequiresAdminAndMapsOptionalTextSafely()
    {
        var restriction = Assert.Single(typeof(SettingsController).GetMethod(nameof(SettingsController.Save))!.GetCustomAttributes(typeof(AuthorizeAttribute), false).Cast<AuthorizeAttribute>());
        Assert.Equal(StaffRoles.Admin, restriction.Roles);
        var form = new SettingsForm { VenueName = "Venue", StaffEmails = "staff@example.test; other@example.test\n" };
        var command = form.Command(); Assert.Equal(2, command.StaffEmails.Length); Assert.Equal("", command.BookingPolicy);
    }
}
