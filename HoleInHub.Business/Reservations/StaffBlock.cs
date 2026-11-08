using System.Security.Cryptography;
using System.Text.Json;
using HoleInHub.Data.Reservations;

namespace HoleInHub.Business.Reservations;

public sealed record StaffBlockRequest(string ResourceId, DateOnly Date, TimeOnly StartTime,
    int DurationMinutes, string Reason, string IdempotencyKey);

public sealed record ScheduleBlockDto(string Id, string Location, DateTime StartUtc, DateTime EndUtc,
    string? Reason, string? CreatedBy, DateTime? CreatedUtc, string? ReleasedBy, DateTime? ReleasedUtc, string? ReleaseReason);

public sealed partial class ReservationService
{
    public async Task<ScheduleBlockDto?> GetScheduleBlockAsync(string id, CancellationToken ct)
    {
        var block = await persistence.BlockByIdAsync(id, ct);
        if (block is null) { return null; }
        var catalog = await GetCatalogAsync(ct);
        var location = block.ResourceIds.Length == 0 ? "All bays" : string.Join(", ", block.ResourceIds
            .Select(resource => catalog.FirstOrDefault(bay => bay.Id == resource)?.Name ?? resource));
        return new(block.Id, location, block.StartUtc, block.EndUtc, block.Reason, block.CreatedBy, block.CreatedUtc,
            block.ReleasedBy, block.ReleasedUtc, block.ReleaseReason);
    }

    public Task<bool> ReleaseScheduleBlockAsync(string id, string reason, string actor, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(actor))
        {
            throw new ReservationValidationException("Open a schedule block before releasing time.");
        }
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 500)
        {
            throw new ReservationValidationException("Enter a release reason of 500 characters or fewer.");
        }
        return persistence.ReleaseBlockAsync(id, actor, reason.Trim(), (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime, ct);
    }

    public async Task<string> CreateScheduleBlockAsync(StaffBlockRequest request, string actor, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(actor) || string.IsNullOrWhiteSpace(request.ResourceId)
            || string.IsNullOrWhiteSpace(request.IdempotencyKey) || request.IdempotencyKey.Length > 100)
        {
            throw new ReservationValidationException("Choose a bay and reopen Block time if the form has expired.");
        }
        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 500)
        {
            throw new ReservationValidationException("Enter a reason of 500 characters or fewer.");
        }
        request = request with { Reason = request.Reason.Trim() };
        var (start, end) = ReservationRules.Interval(request.Date, request.StartTime, request.DurationMinutes);
        var now = (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime;
        var id = "admin-block:" + Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new[] { actor, request.IdempotencyKey })));
        var hash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(request)));
        string Result(ScheduleBlock saved)
        {
            if (saved.SubmissionHash != hash)
            {
                throw new ReservationValidationException("This form already saved a different block. Reopen Block time to create another.");
            }
            if (saved.ReleasedUtc is not null)
            {
                throw new ReservationValidationException("This block has already been released. Reopen Block time to create another.");
            }
            return saved.Id;
        }
        // Check replays before the clock/catalog so an uncertain save can be retried later.
        var prior = await persistence.BlockByIdAsync(id, ct);
        if (prior is not null) { return Result(prior); }
        if (start <= now.AddMinutes(-30))
        {
            throw new ReservationValidationException("Choose the current half-hour or a future time.");
        }
        await ProcessPendingAsync(ct);
        try
        {
            return Result(await persistence.InsertBlockAsync(new ScheduleBlock
            {
                Id = id,
                ResourceIds = [request.ResourceId],
                StartUtc = start,
                EndUtc = end,
                Reason = request.Reason,
                CreatedBy = actor,
                CreatedUtc = now,
                SubmissionHash = hash
            }, ct));
        }
        catch (AllocationConflictException error) { throw new ReservationValidationException(error.Message); }
    }
}
