using System.ComponentModel.DataAnnotations;
using HoleInHub.Web.ViewModels;
using Xunit;

namespace HoleInHub.Tests.Web.Smoke;

public sealed class StaffProfileModelTests
{
    [Theory]
    [InlineData("", "NewPassword123", "NewPassword123", false)]
    [InlineData("old-password!", "short", "short", false)]
    [InlineData("old-password!", "New password123", "New password123", false)]
    [InlineData("old-password!", "NewPassword123!", "NewPassword123!", false)]
    [InlineData("old-password!", "NewPassword123", "Different123", false)]
    [InlineData("old-password!", "NewPassword123", "NewPassword123", true)]
    public void PasswordChangeRequiresCurrentPasswordAndMatchingAlphanumericPassword(string current, string password, string confirm, bool expected)
    {
        var model = new StaffPasswordModel { CurrentPassword = current, Password = password, ConfirmPassword = confirm };
        Assert.Equal(expected, Validator.TryValidateObject(model, new ValidationContext(model), [], true));
    }
}
