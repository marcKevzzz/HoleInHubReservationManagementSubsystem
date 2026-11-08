using System.ComponentModel.DataAnnotations;
using HoleInHub.Business.Reservations;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace HoleInHubReservationManagementSubsystem.ViewModels;

public sealed class ReleaseBlockForm
{
    [Required, StringLength(500)] public string Reason { get; set; } = "";
    [Range(typeof(bool), "true", "true", ErrorMessage = "Confirm that you want to unblock this time.")]
    public bool ConfirmRelease { get; set; }
    [BindNever] public ScheduleBlockDto? Block { get; set; }
}
