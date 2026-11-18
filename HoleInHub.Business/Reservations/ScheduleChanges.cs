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
        string actor, CancellationToken ct, string? resourceId = null, string? sourceResourceId = null, bool confirmVipUpgrade = false)
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
        if (confirmVipUpgrade) { payload = JsonSerializer.SerializeToUtf8Bytes(new { payload = Convert.ToBase64String(payload), confirmVipUpgrade }); }
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
        var target = resourceId is null ? null : (await GetCatalogAsync(ct)).FirstOrDefault(bay => bay.Id == resourceId);
        var upgrading = target?.Service == "VIP" && booking.Service == "Regular";
        ValidateTimelineServiceChange(booking.Service, target?.Service ?? booking.Service, booking.ResourceIds.Length, confirmVipUpgrade);
        var resources = booking.ResourceIds.Select(resource => resource == sourceResourceId ? resourceId! : resource).ToArray();
        var duration = (int)(booking.EndUtc - booking.StartUtc).TotalMinutes;
        var (start, end) = await PreviewBookingIntervalAsync(date, time, duration,
            upgrading ? "VIP" : booking.Service, ct, upgrading ? null : booking.ServiceEntryId, existingBooking: !upgrading);
        var now = (clock ?? TimeProvider.System).GetUtcNow();
        var proposal = new ScheduleChangeRecord { Id = commandId, PayloadHash = hash, StartUtc = start, EndUtc = end };
        try
        {
            await persistence.DecideScheduleChangeAsync(id, commandId, revision, true, resources, actor,
                upgrading ? $"Regular to VIP upgrade: {sourceResourceId} to {resourceId}. Staff confirmed customer agreement to VIP pricing." : "Moved on the staff timeline.",
                now.UtcDateTime, ct, proposal, upgrading, validateCapacity: ValidateStoredCapacity, validateSession: ValidateChangedSession);
        }
        catch (AllocationConflictException error) { throw new ReservationValidationException(error.Message); }
    }

    public static void ValidateTimelineServiceChange(string source, string destination, int bays, bool confirmed)
    {
        if (source == destination) { return; }
        if (source != "Regular" || destination != "VIP" || bays != 1)
        { throw new ReservationValidationException("Only single-bay Regular reservations can be upgraded to VIP."); }
        if (!confirmed) { throw new ReservationValidationException("Confirm the customer's agreement to VIP pricing before upgrading."); }
    }

    public static bool CanRequestScheduleChange(ReservationDto booking, DateTime now) =>
        CanMoveStaffBooking(booking, now) && booking.StartUtc >= now.AddHours(1);

    public static bool CanMoveStaffBooking(ReservationDto booking, DateTime now) => StaffMoveRestriction(booking, now) is null;

    private static string? StaffMoveRestriction(ReservationDto booking, DateTime now)
    {
        if (booking.StartUtc <= now) { return "This reservation has already started and cannot be moved."; }
        if (booking.Status != "Confirmed") { return "Only confirmed reservations can be moved."; }
        if (booking.Service is not ("Regular" or "VIP")) { return "Only Regular and VIP bookings can be moved on the timeline."; }
        if (booking.CancellationRequested) { return "Resolve the pending cancellation request before moving this reservation."; }
        return booking.ScheduleChange?.Status == "Pending" ? "Resolve the pending schedule change before moving this reservation." : null;
    }

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
        var (start, end) = await PreviewBookingIntervalAsync(command.Date, command.StartTime, command.DurationMinutes, booking.Service, ct, booking.ServiceEntryId, existingBooking: true);
        if (start == booking.StartUtc && end == booking.EndUtc)
        {
            throw new ReservationValidationException("Choose a different start time or duration.");
        }
        var now = (clock ?? TimeProvider.System).GetUtcNow();
        await LoadOperationalPolicyAsync(ct);
        effectivePolicy.ValidateCoverage(now);
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
            return await persistence.DecideScheduleChangeAsync(id, proposal, revision, approve, resources, actor, reason?.Trim() ?? "",
                (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime, ct, validateCapacity: ValidateStoredCapacity, validateSession: ValidateChangedSession);
        }
        catch (AllocationConflictException error) { throw new ReservationValidationException(error.Message); }
    }
}
