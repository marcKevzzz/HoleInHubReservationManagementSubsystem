using HoleInHub.Business.Reservations;
namespace HoleInHubReservationManagementSubsystem.ViewModels;

public sealed class MyReservationsViewModel
{
    public IReadOnlyList<ReservationDto> Upcoming { get; init; } = [];
    public IReadOnlyList<ReservationDto> Past { get; init; } = [];
    public bool ShowPast { get; init; }
}
