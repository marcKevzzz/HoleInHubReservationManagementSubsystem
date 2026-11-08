using System.ComponentModel.DataAnnotations;
using HoleInHub.Business.Reservations;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace HoleInHubReservationManagementSubsystem.ViewModels;

public sealed class ScheduleChangeForm
{
    [Required, Range(0, long.MaxValue)] public long? ExpectedRevision { get; set; }
    [Required] public string CommandId { get; set; } = "";
    [Required] public DateOnly? Date { get; set; }
    [Required] public TimeOnly? StartTime { get; set; }
    [Range(30, 840)] public int DurationMinutes { get; set; }
    [Required, StringLength(500)] public string Reason { get; set; } = "";
    [BindNever, ValidateNever] public ReservationDto Booking { get; set; } = null!;
}
