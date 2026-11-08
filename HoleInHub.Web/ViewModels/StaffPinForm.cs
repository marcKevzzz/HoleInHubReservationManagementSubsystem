using System.ComponentModel.DataAnnotations;

namespace HoleInHub.Web.ViewModels;

public sealed class StaffPinForm
{
    [EmailAddress, StringLength(254)]
    [Display(Name = "Work email")]
    public string? Email { get; set; }

    [Required, RegularExpression("[0-9]{6}", ErrorMessage = "Enter exactly six digits.")]
    [Display(Name = "Six-digit PIN")]
    public string Pin { get; set; } = "";
}

public sealed class StaffPinSetupForm
{
    [Required, EmailAddress, StringLength(254)]
    [Display(Name = "Work email")]
    public string Email { get; set; } = "";

    [Required, StringLength(256)]
    public string Password { get; set; } = "";

    [Required, RegularExpression("[0-9]{6}", ErrorMessage = "Enter exactly six digits.")]
    [Display(Name = "New PIN")]
    public string Pin { get; set; } = "";

    [Required, StringLength(6), Compare(nameof(Pin))]
    [Display(Name = "Confirm PIN")]
    public string ConfirmPin { get; set; } = "";
}
