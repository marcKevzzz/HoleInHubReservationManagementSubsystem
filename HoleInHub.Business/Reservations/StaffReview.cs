namespace HoleInHub.Business.Reservations;

public sealed record StaffReviewContext(ReservationDto Booking, bool HasConflict, bool HasEmail, long? PastBookings, long? Cancellations, bool CanCheckIn = false, bool CanCheckOut = false, bool NeedsFollowUp = false)
{
    public string Email { get; init; } = "";
    public IReadOnlyList<ResourceDto> Bays { get; init; } = [];
}

public sealed partial class ReservationService
{
    public Task<IReadOnlyDictionary<string, long>> GetReviewCountsAsync(CancellationToken ct) =>
        persistence.ReviewCountsAsync((clock ?? TimeProvider.System).GetUtcNow().UtcDateTime, ct);

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
        var now = (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime;
        var conflict = record.Status is ("Pending" or "Confirmed") && await persistence.HasReviewConflictAsync(record, ct, now: now);
        var customer = await persistence.ReadCustomerContextAsync(record, now, ct);
        return new(Map(record), conflict, !string.IsNullOrWhiteSpace(record.Email), customer?.Past, customer?.Cancellations,
            !conflict && AttendanceRules.CanCheckIn(record, now),
            record.Status == "Checked-In" && now >= record.StartUtc && (record.CheckedInUtc is null || now >= record.CheckedInUtc),
            record.Status == "Confirmed" && now >= AttendanceRules.FollowUpDue(record.StartUtc, record.EndUtc))
        {
            Email = record.Email,
            Bays = record.Status == "Pending" && record.Participants.Length == 0 && record.Service != "WholeShop"
                ? (await GetCatalogAsync(ct)).Where(bay => record.ResourceIds.Contains(bay.Id)).ToArray() : []
        };
    }
}
