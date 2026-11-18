using System.ComponentModel.DataAnnotations;
using HoleInHub.Web.ViewModels;
namespace HoleInHubReservationManagementSubsystem.ViewModels;

public sealed class CustomerContactForm : AccountContactModel
{
    [EmailAddress, StringLength(254)] public string? Email { get; set; }
    [Required, RegularExpression("^[a-f0-9]{32}$")] public string CommandId { get; set; } = Guid.NewGuid().ToString("N");
}
