using System.ComponentModel.DataAnnotations;
using HoleInHub.Web.Accounts;
using HoleInHub.Web.ViewModels;
using Xunit;

namespace HoleInHub.Tests.Web.Smoke;

public sealed class ProfileDetailsTests
{
    [Fact]
    public void IdentityMappingPreservesSavedProfileDetails()
    {
        var user = new AccountUser { Age = 30, Address = "123 Main Street" };
        var restored = AccountUser.From(user.ToDetails())!;
        Assert.Equal(user.Age, restored.Age);
        Assert.Equal(user.Address, restored.Address);
    }

    [Theory]
    [InlineData(null, null, true)]
    [InlineData(25, "123 Main Street", true)]
    [InlineData(0, "123 Main Street", false)]
    [InlineData(121, "123 Main Street", false)]
    [InlineData(25, "AB", false)]
    [InlineData(25, "A\0B", false)]
    public void ProfilePostValidationRejectsInvalidAgeAndAddress(int? age, string? address, bool expected)
    {
        var model = new AccountProfileModel { FirstName = "Test", LastName = "Player", Phone = "639171234567", Age = age, Address = address };
        Assert.Equal(expected, Validator.TryValidateObject(model, new ValidationContext(model), [], true));
    }
}
