using System.ComponentModel.DataAnnotations;
using HoleInHub.Business.Reservations;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace HoleInHubReservationManagementSubsystem.ViewModels;

public sealed class StaffBookingForm
{
    [StringLength(100)] public string? CustomerProfileId { get; set; }
    public string? ResourceId { get; set; }
    [MaxLength(8)] public string[] ResourceIds { get; set; } = [];
    [Required] public DateOnly? Date { get; set; }
    [Required] public TimeOnly? StartTime { get; set; }
    [Range(30, 840)] public int DurationMinutes { get; set; } = 30;
    [Required, StringLength(100)] public string FirstName { get; set; } = "";
    [Required, StringLength(100)] public string LastName { get; set; } = "";
    [Required, StringLength(15)] public string Phone { get; set; } = "";
    [Range(1, 80)] public int Attendees { get; set; } = 1;
    [Required] public string Source { get; set; } = "Walk-in";
    [Required, StringLength(100)] public string IdempotencyKey { get; set; } = "";
    [BindNever] public IReadOnlyList<ResourceDto> Bays { get; set; } = [];
    [BindNever] public DateOnly Today { get; set; }
}
