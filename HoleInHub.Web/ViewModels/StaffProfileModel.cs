using System.ComponentModel.DataAnnotations;

namespace HoleInHub.Web.ViewModels;

public sealed class StaffProfileModel
{
    public AccountContactModel Contact { get; init; } = new();
    public StaffPasswordModel PasswordChange { get; init; } = new();
    public string Email { get; init; } = "";
    public string Role { get; init; } = "";
    public bool HasPassword { get; init; }
    public bool CanSetPin { get; init; }
}

public sealed class StaffPasswordModel
{
    [Required, StringLength(128), DataType(DataType.Password), Display(Name = "Current password")]
    public string CurrentPassword { get; set; } = "";
    [Required, StringLength(128, MinimumLength = 8), RegularExpression("[A-Za-z0-9]+", ErrorMessage = "Use letters and numbers only."), DataType(DataType.Password), Display(Name = "New password")]
    public string Password { get; set; } = "";
    [Required, Compare(nameof(Password)), DataType(DataType.Password), Display(Name = "Confirm new password")]
    public string ConfirmPassword { get; set; } = "";
}
