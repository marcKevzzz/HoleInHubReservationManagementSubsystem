using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HoleInHub.Data.Reservations;
using Microsoft.Extensions.Options;
namespace HoleInHub.Business.Reservations;

public sealed class ReservationService(ReservationPersistence persistence, OutboxPersistence outbox, IOptions<ReservationPolicy> policy)
{
    public async Task<IReadOnlyList<ResourceDto>> GetCatalogAsync(CancellationToken ct) => (await persistence.CatalogAsync(ct)).Select(x => new ResourceDto(x.Id, x.Name, x.Service)).ToArray();
    public async Task<IReadOnlyList<AvailabilityDto>> GetAvailabilityAsync(DateOnly date, TimeOnly startTime, int durationMinutes, string service, CancellationToken ct) { var (start, end) = ReservationRules.PreviewInterval(date, startTime, durationMinutes, service); await ProcessPendingAsync(ct); var occupied = await persistence.OccupiedAsync(start, end, ct); return (await GetCatalogAsync(ct)).Where(x => service == "WholeShop" || x.Service == (service == "VIP" ? "VIP" : "Regular")).Select(x => new AvailabilityDto(x, !occupied.Contains(x.Id))).ToArray(); }
    public async Task<ReservationDto> SubmitAsync(SubmitReservationRequest request, string accountId, string verifiedEmail, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(accountId) || string.IsNullOrWhiteSpace(verifiedEmail))
        {
            throw new ReservationValidationException("Sign in with a verified email to submit a request.");
        }

        if (request.Service is not ("Regular" or "VIP"))
        {
            throw new ReservationValidationException("Please contact the venue to arrange this service.");
        }

        if (request.ResourceIds == null || request.ResourceIds.Length is < 1 or > 8 || request.ResourceIds.Distinct().Count() != request.ResourceIds.Length || request.Attendees is < 1 or > 100 || string.IsNullOrWhiteSpace(request.FirstName) || request.FirstName.Length > 100 || string.IsNullOrWhiteSpace(request.LastName) || request.LastName.Length > 100 || string.IsNullOrWhiteSpace(request.Phone) || request.Phone.Length > 30 || string.IsNullOrWhiteSpace(request.IdempotencyKey) || request.IdempotencyKey.Length > 100)
        {
            throw new ReservationValidationException("Complete your contact details, player count, and bay selection.");
        }

        request.ResourceIds = request.ResourceIds.Order().ToArray(); var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request))));
        var prior = await persistence.ByKeyAsync(accountId, request.IdempotencyKey, ct); if (prior != null) { if (prior.PayloadHash != hash) { throw new ReservationValidationException("This submission key was already used for another request. Reload the form."); } return Map(prior); }
        var now = DateTime.UtcNow; policy.Value.ValidateCoverage(new DateTimeOffset(now)); var (start, end) = ReservationRules.Interval(request.Date, request.StartTime, request.DurationMinutes); if (start <= now)
        {
            throw new ReservationValidationException("Choose a future reservation time.");
        }

        await ProcessPendingAsync(ct);
        var record = new ReservationRecord { AccountId = accountId, Email = verifiedEmail, Reference = $"HIH-{Guid.NewGuid().ToString("N")[..10].ToUpperInvariant()}", Service = request.Service, StartUtc = start, EndUtc = end, CreatedUtc = now, ReviewDeadlineUtc = now.AddMinutes(30), ResourceIds = request.ResourceIds, FirstName = request.FirstName.Trim(), LastName = request.LastName.Trim(), Phone = request.Phone.Trim(), Attendees = request.Attendees, IdempotencyKey = request.IdempotencyKey, PayloadHash = hash, History = [new HistoryRecord { Status = "Pending", AtUtc = now, Actor = "Customer" }] };
        try { var saved = await persistence.InsertAsync(record, ct); if (saved.PayloadHash != hash) { throw new ReservationValidationException("This submission key was already used for another request."); } return Map(saved); } catch (AllocationConflictException e) { throw new ReservationValidationException(e.Message); }
    }
    public async Task<IReadOnlyList<ReservationDto>> GetOwnedAsync(string accountId, CancellationToken ct) => (await persistence.OwnedAsync(accountId, ct)).Select(Map).ToArray();
    public async Task<ReservationDto?> GetOwnedAsync(string accountId, string id, CancellationToken ct) { var record = await persistence.OwnedAsync(accountId, id, ct); return record == null ? null : Map(record); }
    public async Task ProcessPendingAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow; await persistence.ExpireAsync(now, policy.Value.StaffEmails, ct); foreach (var record in await persistence.PendingAsync(ct))
        {
            foreach (var minutes in new[] { 15, 25 })
            {
                if (record.CreatedUtc.AddMinutes(minutes) <= now && record.ReviewDeadlineUtc > now && (minutes != 15 || now < record.CreatedUtc.AddMinutes(25)))
                {
                    foreach (var recipient in policy.Value.StaffEmails)
                    {
                        await outbox.EnqueueAsync(new OutboxRecord { Id = $"{record.Id}:review{minutes}:{recipient}", Recipient = recipient, Subject = $"Pending request: {record.Reference}", Body = $"Request {record.Reference} has awaited review for {minutes} minutes. Review deadline: {record.ReviewDeadlineUtc:O}.", DueUtc = now, ReservationId = record.Id, RequiredStatus = "Pending" }, ct);
                    }
                }
            }
        }
    }
    private static ReservationDto Map(ReservationRecord x) => new(x.Id, x.Reference, x.Service, x.Status, x.StartUtc, x.EndUtc, x.ReviewDeadlineUtc, x.ResourceIds, x.FirstName, x.LastName, x.Phone, x.Attendees, x.History.Select(h => new ReservationHistoryDto(h.Status, h.AtUtc, h.Actor)).ToArray());
}


