using System.ComponentModel.DataAnnotations;
using HoleInHub.Business.Accounts;

namespace HoleInHub.Web.ViewModels;

public class AccountContactModel
{
    [Required, StringLength(100), RegularExpression(ContactRules.NamePattern, ErrorMessage = ContactRules.NameError), Display(Name = "First name")] public string FirstName { get; set; } = "";
    [Required, StringLength(100), RegularExpression(ContactRules.NamePattern, ErrorMessage = ContactRules.NameError), Display(Name = "Last name")] public string LastName { get; set; } = "";
    [Required, StringLength(15), RegularExpression(ContactRules.PhonePattern, ErrorMessage = ContactRules.PhoneError), Display(Name = "Contact number")] public string Phone { get; set; } = "";
}
public sealed class SignUpModel : AccountContactModel
{
    [StringLength(2048)] public string? ReturnUrl { get; set; }
    [Required, EmailAddress, StringLength(254)] public string Email { get; set; } = "";
    [Required, StringLength(128, MinimumLength = 12), DataType(DataType.Password)] public string Password { get; set; } = "";
    [Required, Compare(nameof(Password)), DataType(DataType.Password), Display(Name = "Confirm password")] public string ConfirmPassword { get; set; } = "";
}
public sealed class GoogleSignUpModel : AccountContactModel
{
    [StringLength(2048)] public string? ReturnUrl { get; set; }
    public string Email { get; set; } = "";
}
public sealed class SignInModel
{
    [Required, EmailAddress, StringLength(254)] public string Email { get; set; } = "";
    [Required, StringLength(128), DataType(DataType.Password)] public string Password { get; set; } = "";
    [StringLength(2048)] public string? ReturnUrl { get; set; }
}
public sealed class EmailModel
{
    [StringLength(2048)] public string? ReturnUrl { get; set; }
    [Required, EmailAddress, StringLength(254)] public string Email { get; set; } = "";
}
public sealed class ResetPasswordModel
{
    [StringLength(2048)] public string? ReturnUrl { get; set; }
    [Required, StringLength(64)] public string UserId { get; set; } = "";
    [Required, StringLength(4096)] public string Token { get; set; } = "";
    [Required, StringLength(128, MinimumLength = 12), DataType(DataType.Password)] public string Password { get; set; } = "";
    [Required, Compare(nameof(Password)), DataType(DataType.Password), Display(Name = "Confirm password")] public string ConfirmPassword { get; set; } = "";
}
