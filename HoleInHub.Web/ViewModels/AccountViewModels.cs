using System.ComponentModel.DataAnnotations;

namespace HoleInHub.Web.ViewModels;

public sealed class SignUpModel
{
    [Required, EmailAddress, StringLength(254)] public string Email { get; set; } = "";
    [Required, StringLength(128, MinimumLength = 12), DataType(DataType.Password)] public string Password { get; set; } = "";
    [Required, Compare(nameof(Password)), DataType(DataType.Password), Display(Name = "Confirm password")] public string ConfirmPassword { get; set; } = "";
}
public sealed class SignInModel
{
    [Required, EmailAddress, StringLength(254)] public string Email { get; set; } = "";
    [Required, StringLength(128), DataType(DataType.Password)] public string Password { get; set; } = "";
    public string? ReturnUrl { get; set; }
}
public sealed class EmailModel
{
    [Required, EmailAddress, StringLength(254)] public string Email { get; set; } = "";
}
public sealed class ResetPasswordModel
{
    [Required, StringLength(64)] public string UserId { get; set; } = "";
    [Required, StringLength(4096)] public string Token { get; set; } = "";
    [Required, StringLength(128, MinimumLength = 12), DataType(DataType.Password)] public string Password { get; set; } = "";
    [Required, Compare(nameof(Password)), DataType(DataType.Password), Display(Name = "Confirm password")] public string ConfirmPassword { get; set; } = "";
}
