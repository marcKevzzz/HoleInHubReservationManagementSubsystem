using HoleInHub.Business.Accounts;
using Xunit;
namespace HoleInHub.Tests.Unit.Accounts;

public sealed class TeamCreationRulesTests
{
    [Theory]
    [InlineData("Admin", "Admin", true)]
    [InlineData("Admin", "Manager", true)]
    [InlineData("Admin", "Staff", true)]
    [InlineData("Manager", "Manager", true)]
    [InlineData("Manager", "Staff", true)]
    [InlineData("Manager", "Admin", false)]
    [InlineData("Staff", "Staff", false)]
    [InlineData("Customer", "Manager", false)]
    [InlineData("Admin", "Owner", false)]
    public void TeamCreationHonorsTheRoleBoundary(string actor, string requested, bool expected) =>
        Assert.Equal(expected, StaffRoles.CanCreate(actor, requested));
}
