using System.ComponentModel.DataAnnotations;
namespace HoleInHubReservationManagementSubsystem.ViewModels;

public sealed class StaffPinResetForm
{
    [Required, StringLength(128)] public string Version { get; set; } = "";
    [Required, RegularExpression("^[a-f0-9]{32}$")] public string CommandId { get; set; } = "";
    [Required, StringLength(500, MinimumLength = 1)] public string Reason { get; set; } = "";
}
