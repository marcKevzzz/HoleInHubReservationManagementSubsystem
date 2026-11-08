using System.ComponentModel.DataAnnotations;

namespace HoleInHubReservationManagementSubsystem.ViewModels;

public sealed class StaffAttendanceForm
{
    [Required] public string Action { get; set; } = "";
    [Required, Range(0, long.MaxValue)] public long? ExpectedRevision { get; set; }
    [Required, StringLength(32, MinimumLength = 32)] public string CommandId { get; set; } = "";
    [StringLength(500)] public string? Reason { get; set; }
    [Range(typeof(bool), "true", "true", ErrorMessage = "Confirm the attendance action before saving.")]
    public bool ConfirmAttendance { get; set; }
}
