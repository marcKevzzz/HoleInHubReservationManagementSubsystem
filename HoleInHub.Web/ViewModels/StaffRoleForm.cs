
using System.ComponentModel.DataAnnotations;
using HoleInHub.Business.Accounts;

namespace HoleInHubReservationManagementSubsystem.ViewModels;

public sealed class StaffRoleForm
{
    public const string NoStaffAccess = "None";

    [Required, StringLength(128)]
    public string Version { get; set; } = "";

    [Required(ErrorMessage = "Choose a staff role or No staff access.")]
    [StringLength(32)]
    public string Role { get; set; } = "";

    [Required(ErrorMessage = "Enter a reason for the access change.")]
    public string Reason { get; set; } = "";
}

public sealed record StaffRoleEditModel(AccountDirectoryEntry Account, StaffRoleForm Form, bool IsStale = false);
