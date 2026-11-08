using HoleInHubReservationManagementSubsystem.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace HoleInHub.Tests.Web.Smoke;

public sealed class StaffBlockAccessTests
{
    [Fact]
    public void BothBlockEndpointsRequireVenueManagementAndPostRequiresAntiforgery()
    {
        var actions = typeof(BookingReviewController).GetMethods().Where(x => x.Name == "CreateBlock").ToArray();
        Assert.Equal(2, actions.Length);
        foreach (var action in actions)
        {
            Assert.Equal("Manager,Admin", Assert.Single(action.GetCustomAttributes(typeof(AuthorizeAttribute), false).Cast<AuthorizeAttribute>()).Roles);
        }
        var post = Assert.Single(actions, x => x.IsDefined(typeof(HttpPostAttribute), false));
        Assert.True(post.IsDefined(typeof(ValidateAntiForgeryTokenAttribute), false));
    }
    [Fact]
    public void ReleaseAndDetailsRequireVenueManagementAndReleaseRequiresAntiforgery()
    {
        foreach (var name in new[] { "Block", "ReleaseBlock" })
        {
            var action = typeof(BookingReviewController).GetMethod(name)!;
            Assert.Equal("Manager,Admin", Assert.Single(action.GetCustomAttributes(typeof(AuthorizeAttribute), false).Cast<AuthorizeAttribute>()).Roles);
        }
        Assert.True(typeof(BookingReviewController).GetMethod("ReleaseBlock")!.IsDefined(typeof(ValidateAntiForgeryTokenAttribute), false));
    }

}
