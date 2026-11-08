namespace HoleInHub.Business.Reservations;

public sealed record StaffReviewContext(ReservationDto Booking, bool HasConflict, bool HasEmail, long? PastBookings, long? Cancellations, bool CanCheckIn = false, bool CanCheckOut = false, bool NeedsFollowUp = false);

public sealed partial class ReservationService
{
    public Task<long> GetPendingReviewCountAsync(CancellationToken ct) =>
        persistence.PendingReviewCountAsync((clock ?? TimeProvider.System).GetUtcNow().UtcDateTime, ct);

    public async Task<StaffReviewContext?> GetStaffReviewAsync(string id, CancellationToken ct)
    {
        await ProcessPendingAsync(ct);
        var record = await persistence.ByIdAsync(id, ct);
        if (record is null)
        {
            return null;
        }
        var conflict = record.Status is ("Pending" or "Confirmed") && await persistence.HasReviewConflictAsync(record, ct);
        var customer = await persistence.ReadCustomerContextAsync(record, (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime, ct);
        var now = (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime;
        return new(Map(record), conflict, !string.IsNullOrWhiteSpace(record.Email), customer?.Past, customer?.Cancellations,
            !conflict && AttendanceRules.CanCheckIn(record, now),
            record.Status == "Checked-In" && now >= record.StartUtc && (record.CheckedInUtc is null || now >= record.CheckedInUtc),
            record.Status == "Confirmed" && now >= AttendanceRules.FollowUpDue(record.StartUtc, record.EndUtc));
    }
}
