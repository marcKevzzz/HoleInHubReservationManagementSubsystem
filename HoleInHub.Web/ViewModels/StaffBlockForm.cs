using System.ComponentModel.DataAnnotations;
using HoleInHub.Business.Reservations;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace HoleInHubReservationManagementSubsystem.ViewModels;

public sealed class StaffBlockForm
{
    [Required] public string ResourceId { get; set; } = "";
    [Required] public DateOnly? Date { get; set; }
    [Required] public TimeOnly? StartTime { get; set; }
    [Range(30, 840)] public int DurationMinutes { get; set; } = 30;
    [Required, StringLength(500)] public string Reason { get; set; } = "";
    [Required, StringLength(100)] public string IdempotencyKey { get; set; } = "";
    [BindNever] public IReadOnlyList<ResourceDto> Bays { get; set; } = [];
    [BindNever] public DateOnly Today { get; set; }
}
