namespace HoleInHub.Business.Reservations;

public sealed record GuestReservationHistory(IReadOnlyList<ReservationDto> Items, long UpcomingCount, long PastCount, bool HasMore);

public sealed partial class ReservationService
{
    public async Task<GuestReservationHistory> GetGuestHistoryAsync(string email, bool past, int page, CancellationToken ct)
    {
        await ProcessPendingAsync(ct);
        var result = await persistence.GuestHistoryAsync(email, past, (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime, page, ct);
        return new(result.Items.Select(Map).ToArray(), result.UpcomingCount, result.PastCount, result.HasMore);
    }

    public async Task<ReservationDto?> GetGuestDetailAsync(string email, string id, CancellationToken ct)
    {
        var record = await persistence.GuestDetailAsync(email, id, ct);
        return record is null ? null : Map(record);
    }
}
