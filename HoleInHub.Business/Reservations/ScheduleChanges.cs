using System.Security.Cryptography;
using System.Text.Json;
using HoleInHub.Data.Reservations;

namespace HoleInHub.Business.Reservations;

public sealed record RequestScheduleChange(string BookingId, long ExpectedRevision, string CommandId, DateOnly Date,
    TimeOnly StartTime, int DurationMinutes, string Reason);
public sealed record ScheduleChangeDto(string Id, string Status, DateTime RequestedUtc, DateTime? DecidedUtc,
    DateTime StartUtc, DateTime EndUtc, string Reason, string[] ResourceIds, Dictionary<string, string> ResourceNames);

public sealed partial class ReservationService
{
    public async Task MoveStaffBookingAsync(string id, long revision, string commandId, DateOnly date, TimeOnly time,
        string actor, CancellationToken ct, string? resourceId = null, string? sourceResourceId = null)
    {
        if (revision < 0 || !Guid.TryParseExact(commandId, "N", out _) || string.IsNullOrWhiteSpace(actor))
        {
            throw new ReservationValidationException("Reload the schedule before moving this booking.");
        }
        var saved = await persistence.ByIdAsync(id, ct)
            ?? throw new ReservationValidationException("This booking is no longer available.");
        var booking = Map(saved);
        var payload = resourceId is null && sourceResourceId is null
            ? JsonSerializer.SerializeToUtf8Bytes(new { id, revision, commandId, date, time, actor })
            : JsonSerializer.SerializeToUtf8Bytes(new { id, revision, commandId, date, time, actor, resourceId, sourceResourceId });
        var hash = Convert.ToHexString(SHA256.HashData(payload));
        var prior = saved.History.FirstOrDefault(entry => entry.CommandId == commandId);
        if (prior is not null)
        {
            if (prior.CommandHash != hash) { throw new ReservationValidationException("This move was submitted with different details. Reload the schedule."); }
            return;
        }
        if ((resourceId is not null || sourceResourceId is not null)
            && (string.IsNullOrWhiteSpace(resourceId) || !booking.ResourceIds.Contains(sourceResourceId)))
        {
            throw new ReservationValidationException("Choose a bay from this booking and an active bay of the same type.");
        }
        var resources = booking.ResourceIds.Select(resource => resource == sourceResourceId ? resourceId! : resource).ToArray();
        var duration = (int)(booking.EndUtc - booking.StartUtc).TotalMinutes;
        var (start, end) = ReservationRules.PreviewInterval(date, time, duration, booking.Service);
        var now = (clock ?? TimeProvider.System).GetUtcNow();
        var proposal = new ScheduleChangeRecord { Id = commandId, PayloadHash = hash, StartUtc = start, EndUtc = end };
        try
        {
            await persistence.DecideScheduleChangeAsync(id, commandId, revision, true, resources, actor,
                "Moved on the staff timeline.", now.UtcDateTime, ct, proposal);
        }
        catch (AllocationConflictException error) { throw new ReservationValidationException(error.Message); }
    }

    public static bool CanRequestScheduleChange(ReservationDto booking, DateTime now) =>
        CanMoveStaffBooking(booking, now) && booking.StartUtc >= now.AddHours(1);

    public static bool CanMoveStaffBooking(ReservationDto booking, DateTime now) =>
        booking.Status == "Confirmed" && booking.Service is ("Regular" or "VIP") && !booking.CancellationRequested
        && booking.ScheduleChange?.Status != "Pending" && booking.StartUtc > now;

    public async Task RequestScheduleChangeAsync(RequestScheduleChange command, string actor, CancellationToken ct)
    {
        if (command.ExpectedRevision < 0 || !Guid.TryParseExact(command.CommandId, "N", out _)
            || string.IsNullOrWhiteSpace(actor) || string.IsNullOrWhiteSpace(command.Reason) || command.Reason.Length > 500)
        {
            throw new ReservationValidationException("Enter a reason of up to 500 characters and reload if this form is out of date.");
        }
        var hash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(command)));
        var saved = await persistence.ByIdAsync(command.BookingId, ct)
            ?? throw new ReservationValidationException("This booking is no longer available.");
        var prior = saved.History.FirstOrDefault(entry => entry.CommandId == command.CommandId);
        if (prior is not null)
        {
            if (prior.CommandHash != hash) { throw new ReservationValidationException("This form was submitted with different details. Reload the booking."); }
            return;
        }
        var booking = Map(saved);
        var (start, end) = ReservationRules.PreviewInterval(command.Date, command.StartTime, command.DurationMinutes, booking.Service);
        if (start == booking.StartUtc && end == booking.EndUtc)
        {
            throw new ReservationValidationException("Choose a different start time or duration.");
        }
        var now = (clock ?? TimeProvider.System).GetUtcNow();
        policy.Value.ValidateCoverage(now);
        GetReviewDeadline(booking.Service, start, now);
        var proposal = new ScheduleChangeRecord
        {
            Id = command.CommandId,
            PayloadHash = hash,
            StartUtc = start,
            EndUtc = end,
            Reason = command.Reason.Trim(),
            RequestedUtc = now.UtcDateTime
        };
        try
        {
            await persistence.RequestScheduleChangeAsync(command.BookingId, command.ExpectedRevision, proposal, actor,
                current => CanRequestScheduleChange(Map(current), (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime), ct);
        }
        catch (AllocationConflictException error) { throw new ReservationValidationException(error.Message); }
    }

    public async Task<(string Reference, DateTime StartUtc, DateTime EndUtc)> DecideScheduleChangeAsync(string id, string proposal, long revision, bool approve,
        string[] resources, string actor, string? reason, CancellationToken ct)
    {
        if (revision < 0 || !Guid.TryParseExact(proposal, "N", out _) || (approve && resources.Length is (< 1 or > 8))
            || string.IsNullOrWhiteSpace(actor) || reason?.Length > 500 || (!approve && string.IsNullOrWhiteSpace(reason)))
        {
            throw new ReservationValidationException("Check the bay selection and enter a reason when declining (up to 500 characters).");
        }
        try
        {
            if (approve)
            {
                var booking = await GetStaffAsync(id, ct)
                    ?? throw new ReservationValidationException("This booking is no longer available.");
                if (booking.Participants.Length > 0)
                {
                    BookingRules.ValidateParticipants(booking.Service, booking.ResourceIds, booking.Participants);
                }
                BookingRules.ValidateGuests(booking.Service, resources, booking.Attendees);
            }
            return await persistence.DecideScheduleChangeAsync(id, proposal, revision, approve, resources, actor, reason?.Trim() ?? "",
                (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime, ct);
        }
        catch (AllocationConflictException error) { throw new ReservationValidationException(error.Message); }
    }
}
