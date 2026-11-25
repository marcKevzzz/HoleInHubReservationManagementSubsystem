using HoleInHub.Business.Reservations;
namespace HoleInHubReservationManagementSubsystem.ViewModels;

public sealed class MyReservationsViewModel
{
    public IReadOnlyList<ReservationDto> Upcoming { get; init; } = [];
    public IReadOnlyList<ReservationDto> Past { get; init; } = [];
    public string Filter { get; init; } = "all";
    public IReadOnlyList<ReservationDto> Items => Filter switch
    {
        "past" => Past,
        "upcoming" => Upcoming,
        "completed" => Upcoming.Concat(Past).Where(booking => booking.Status == "Completed").ToArray(),
        "cancelled" => Upcoming.Concat(Past).Where(booking => booking.Status == "Cancelled").ToArray(),
        "expired" => Upcoming.Concat(Past).Where(booking => booking.Status == "Expired").ToArray(),
        _ => Upcoming.Concat(Past).OrderByDescending(booking => booking.StartUtc).ToArray(),
    };
}
