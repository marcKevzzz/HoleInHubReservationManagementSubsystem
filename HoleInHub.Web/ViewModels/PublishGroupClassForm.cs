using System.ComponentModel.DataAnnotations;
using HoleInHub.Business.Reservations;

namespace HoleInHubReservationManagementSubsystem.ViewModels;

public sealed class PublishGroupClassForm
{
    [Required] public string Offering { get; set; } = "";
    [Required, Range(typeof(decimal), "0.01", "9999999")] public decimal? Price { get; set; }
    public StaffClassOffering[] Offerings { get; set; } = [];
    [Required, DataType(DataType.Date)] public DateOnly? Date { get; set; }
    [Required, Range(1, 8)] public int? Capacity { get; set; }
    [MinLength(1, ErrorMessage = "Choose at least one regular bay.")] public string[] BayIds { get; set; } = [];
    public IReadOnlyList<ResourceDto> Bays { get; set; } = [];
}
