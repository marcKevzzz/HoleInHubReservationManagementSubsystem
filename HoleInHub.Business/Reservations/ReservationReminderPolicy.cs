using System.Globalization;
using System.Net.Mail;
using HoleInHub.Data.Reservations;

namespace HoleInHub.Business.Reservations;

public static class ReservationReminderPolicy
{
    public static OutboxRecord? Create(ReservationRecord reservation, DateTime now)
    {
        if (reservation.Status != "Confirmed" || reservation.CheckedInUtc is not null || reservation.CancellationRequested
            || reservation.StartUtc <= now || reservation.StartUtc > now.AddMinutes(15)
            || !MailAddress.TryCreate(reservation.Email, out var address) || address.Address != reservation.Email)
        {
            return null;
        }

        var localStart = TimeZoneInfo.ConvertTimeFromUtc(reservation.StartUtc, ReservationRules.Manila);
        return new OutboxRecord
        {
            Id = $"{reservation.Id}:reminder:{reservation.StartUtc.Ticks.ToString(CultureInfo.InvariantCulture)}",
            ReservationId = reservation.Id,
            RequiredStatus = "Confirmed",
            ReminderStartUtc = reservation.StartUtc,
            Recipient = reservation.Email,
            Subject = $"Upcoming reservation: {reservation.Reference}",
            Body = $"Reminder: your confirmed reservation {reservation.Reference} starts on {localStart.ToString("MMMM d, yyyy 'at' h:mm tt", CultureInfo.InvariantCulture)} (Manila time). We look forward to seeing you at Hole In Hub.",
            DueUtc = now
        };
    }
}
