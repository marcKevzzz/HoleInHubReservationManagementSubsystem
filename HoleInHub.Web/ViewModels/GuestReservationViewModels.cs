using System.ComponentModel.DataAnnotations;
using HoleInHub.Business.Reservations;

namespace HoleInHub.Web.ViewModels;

public sealed class FindGuestReservationsModel
{
    [Required, EmailAddress, StringLength(254)] public string Email { get; set; } = "";
    public string? ReturnUrl { get; set; }
}

public sealed record GuestReservationsModel(string Email, GuestReservationHistory History, bool ShowPast, int Page);
