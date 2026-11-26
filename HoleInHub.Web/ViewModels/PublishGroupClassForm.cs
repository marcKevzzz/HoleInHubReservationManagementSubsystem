using System.ComponentModel.DataAnnotations;
using HoleInHub.Business.Reservations;

namespace HoleInHubReservationManagementSubsystem.ViewModels;

public sealed class PublishGroupClassForm
{
    public string? Editing { get; set; }
    public bool WeeklyPackage { get; set; }
    public long Revision { get; set; }
    [Required] public string Offering { get; set; } = "";
    public StaffClassOffering[] Offerings { get; set; } = [];
    [Required] public TimeOnly? StartTime { get; set; } = new TimeOnly(15, 0);
    [Required] public TimeOnly? EndTime { get; set; } = new TimeOnly(16, 0);
    [Required, DataType(DataType.Date)] public DateOnly? Date { get; set; }
    [Required, Range(1, 10)] public int? Capacity { get; set; }
    [MinLength(1, ErrorMessage = "Choose at least one regular bay.")] public string[] BayIds { get; set; } = [];
    public IReadOnlyList<ResourceDto> Bays { get; set; } = [];
}
