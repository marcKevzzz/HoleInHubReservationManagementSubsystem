using System.ComponentModel.DataAnnotations;
using HoleInHub.Business.Accounts;
using HoleInHub.Web.ViewModels;

namespace HoleInHubReservationManagementSubsystem.ViewModels;

public sealed class TeamMemberForm : AccountContactModel
{
    [Required, EmailAddress, StringLength(254)] public string Email { get; set; } = "";
    [Required] public string Role { get; set; } = StaffRoles.Staff;
    [Required, RegularExpression("^[a-f0-9]{32}$")] public string CommandId { get; set; } = Guid.NewGuid().ToString("N");
}
