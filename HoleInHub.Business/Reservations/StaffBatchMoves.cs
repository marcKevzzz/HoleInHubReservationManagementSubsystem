using System.Security.Cryptography;
using System.Text.Json;
using HoleInHub.Data.Reservations;

namespace HoleInHub.Business.Reservations;

public sealed record StaffBatchMoveItem(string Id, long? Revision);

public sealed partial class ReservationService
{
    public async Task MoveStaffBatchAsync(IReadOnlyList<StaffBatchMoveItem> bookings, string commandId,
        DateOnly date, int offsetMinutes, string actor, CancellationToken ct)
    {
        if (bookings.Count is < 2 or > 20 || bookings.Any(item => string.IsNullOrWhiteSpace(item.Id)
            || item.Id.Length > 64 || item.Revision is null or < 0)
            || bookings.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != bookings.Count
            || !Guid.TryParseExact(commandId, "N", out _) || string.IsNullOrWhiteSpace(actor)
            || date == DateOnly.MinValue || date == DateOnly.MaxValue
            || offsetMinutes is 0 or <= -1440 or >= 1440 || offsetMinutes % 30 != 0)
        {
            throw new ReservationValidationException("Select 2 to 20 bookings and move them in 30-minute steps on the same day.");
        }

        var selected = bookings.OrderBy(item => item.Id, StringComparer.Ordinal)
            .Select(item => new BatchMoveRevision(item.Id, item.Revision!.Value)).ToArray();
        var hash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(
            new { kind = "batch-move", selected, date, offsetMinutes, actor })));
        var now = (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime;

        (DateTime Start, DateTime End) Destination(ReservationRecord record)
        {
            var booking = Map(record);
            if (!CanMoveStaffBooking(booking, now))
            {
                throw new ReservationValidationException($"Booking {booking.Reference} is no longer eligible to move.");
            }
            var start = TimeZoneInfo.ConvertTimeFromUtc(booking.StartUtc, ReservationRules.Manila);
            var target = start.AddMinutes(offsetMinutes);
            if (DateOnly.FromDateTime(start) != date || DateOnly.FromDateTime(target) != date)
            {
                throw new ReservationValidationException($"Booking {booking.Reference} must stay on the selected day.");
            }
            var duration = (int)(booking.EndUtc - booking.StartUtc).TotalMinutes;
            try
            {
                var interval = ReservationRules.PreviewInterval(date, TimeOnly.FromDateTime(target), duration, booking.Service);
                if (interval.Start <= now)
                {
                    throw new ReservationValidationException("Choose a time that has not started.");
                }
                return interval;
            }
            catch (ReservationValidationException error)
            {
                throw new ReservationValidationException($"Booking {booking.Reference}: {error.Message}");
            }
        }

        try
        {
            await persistence.MoveStaffBatchAsync(selected, commandId, hash, actor, date.ToString("yyyy-MM-dd",
                System.Globalization.CultureInfo.InvariantCulture), now, Destination, ct);
        }
        catch (AllocationConflictException error)
        {
            throw new ReservationValidationException(error.Message);
        }
    }
}
