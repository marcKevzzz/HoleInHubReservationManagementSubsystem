using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HoleInHub.Data.Reservations;
using Microsoft.Extensions.Options;
namespace HoleInHub.Business.Reservations;

public sealed partial class ReservationService(ReservationPersistence persistence, OutboxPersistence outbox, IOptions<ReservationPolicy> policy, TimeProvider? clock = null)
{
    public async Task<IReadOnlyList<ResourceDto>> GetCatalogAsync(CancellationToken ct) => (await persistence.CatalogAsync(ct)).Select(x => new ResourceDto(x.Id, x.Name, x.Service)).ToArray();
    public async Task<IReadOnlyList<AvailabilityDto>> GetAvailabilityAsync(DateOnly date, TimeOnly startTime, int durationMinutes, string service, CancellationToken ct) { var (start, end) = ReservationRules.PreviewInterval(date, startTime, durationMinutes, service); BookingRules.ValidateLeadTime(service, start, (clock ?? TimeProvider.System).GetUtcNow()); await ProcessPendingAsync(ct); var occupied = await persistence.OccupiedAsync(start, end, ct, (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime); return (await GetCatalogAsync(ct)).Where(x => service == "WholeShop" || x.Service == (service == "VIP" ? "VIP" : "Regular")).Select(x => new AvailabilityDto(x, !occupied.Contains(x.Id))).ToArray(); }
    public async Task<ReservationDto> SubmitAsync(SubmitReservationRequest request, string accountId, string verifiedEmail, CancellationToken ct, string? guestSessionHash = null, string? accessTokenHash = null, bool demoAllowed = false, string? encryptedReceipt = null)
    {
        if (string.IsNullOrWhiteSpace(accountId) || !System.Net.Mail.MailAddress.TryCreate(verifiedEmail, out var address) || address.Address != verifiedEmail)
        {
            throw new ReservationValidationException("Verify your email before submitting a request.");
        }

        ValidateDetails(request, demoAllowed);
        var hash = Fingerprint(request, verifiedEmail);
        var prior = await persistence.ByKeyAsync(accountId, request.IdempotencyKey, ct); if (prior != null) { if (prior.SubmissionHash != hash) { throw new ReservationValidationException("This submission key was already used for another request. Reload the form."); } return Map(prior); }
        var now = (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime; policy.Value.ValidateCoverage(new DateTimeOffset(now), BookingRules.ReviewMinutes(request.Service)); var (start, end) = ReservationRules.PreviewInterval(request.Date, request.StartTime, request.DurationMinutes, request.Service); BookingRules.ValidateWindow(request.Service, start, new DateTimeOffset(now)); if (start <= now)
        {
            throw new ReservationValidationException("Choose a future reservation time.");
        }

        await ProcessPendingAsync(ct);
        var reviewDeadline = GetReviewDeadline(request.Service, start, new DateTimeOffset(now));
        var reviewStart = request.Service == "WholeShop" ? (DateTime?)null : ReviewClock.AddMinutes(new DateTimeOffset(now), 0, policy.Value);
        var alertMinutes = request.Service == "Regular" ? new[] { 15, 25 } : new[] { 30, 50 };
        var record = new ReservationRecord
        {
            PolicyVersion = request.Participants.Length == 0 ? 2 : 1,
            EmailVerificationId = guestSessionHash,
            AccessTokenHash = accessTokenHash,
            Participants = request.Participants.Select(x => new ParticipantRecord { ResourceId = x.ResourceId, Players = x.Players, NonPlayers = x.NonPlayers }).ToArray(),
            RentalTotal = request.Service == "WholeShop" ? BookingRules.DemoRentalTotal(request.Date, request.DurationMinutes) : null,
            DepositAmount = request.Service == "WholeShop" ? BookingRules.DemoRentalTotal(request.Date, request.DurationMinutes) / 2 : null,
            PaymentStatus = request.Service == "WholeShop" ? "AwaitingDeposit" : "NotRequired",
            AccountId = accountId,
            Email = verifiedEmail,
            Reference = $"HIH-{Guid.NewGuid().ToString("N")[..10].ToUpperInvariant()}",
            Service = request.Service,
            StartUtc = start,
            EndUtc = end,
            CreatedUtc = now,
            ReviewDeadlineUtc = reviewDeadline,
            ReviewStartsUtc = reviewStart,
            ReviewAlertsUtc = reviewStart.HasValue ? alertMinutes.Select(minutes => ReviewClock.AddMinutes(new DateTimeOffset(now), minutes, policy.Value)).ToArray() : null,
            ResourceIds = request.ResourceIds,
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            Phone = request.Phone.Trim(),
            Attendees = request.Attendees,
            Notes = request.Notes?.Trim() ?? "",
            PrivacyAccepted = request.PrivacyAccepted,
            IdempotencyKey = request.IdempotencyKey,
            SubmissionHash = hash,
            History = [new HistoryRecord { EventType = "SubmissionCreated", ReservationStatus = "Pending", Status = "Pending", AtUtc = now, Actor = "Customer" }]
        };
        try { var saved = await persistence.InsertAsync(record, ct, encryptedReceipt); if (saved.SubmissionHash != hash) { throw new ReservationValidationException("This submission key was already used for another request."); } return Map(saved); } catch (AllocationConflictException e) { throw new ReservationValidationException(e.Message); }
    }
    private static string Fingerprint(SubmitReservationRequest request, string email)
    {
        request.Email = email;
        request.Participants = request.Participants.OrderBy(x => x.ResourceId).ToArray();
        request.ResourceIds = request.ResourceIds.Order().ToArray();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request))));
    }
    public async Task<ReservationDto?> FindSubmittedAsync(SubmitReservationRequest request, string owner, string email, bool demoAllowed, CancellationToken ct)
    {
        ValidateDetails(request, demoAllowed);
        var prior = await persistence.ByKeyAsync(owner, request.IdempotencyKey, ct);
        if (prior == null) { return null; }
        if (prior.SubmissionHash != Fingerprint(request, email)) { throw new ReservationValidationException("This submission was already received with different details. Start a new request to make another booking."); }
        return Map(prior);
    }
    private void ValidateDetails(SubmitReservationRequest request, bool demoAllowed)
    {
        if (request.Service is not ("Regular" or "VIP" or "PhilippineCoach" or "KpgaPro" or "KpgaGroup" or "WholeShop"))
        {
            throw new ReservationValidationException("Choose a listed experience.");
        }

        if (request.Service == "WholeShop" && (!demoAllowed || !policy.Value.DemoPaymentsEnabled))
        {
            throw new ReservationValidationException("Whole-shop online checkout is not enabled. Please contact the venue.");
        }

        if (!HoleInHub.Business.Accounts.ContactRules.IsName(request.FirstName) || !HoleInHub.Business.Accounts.ContactRules.IsName(request.LastName))
        {
            throw new ReservationValidationException(HoleInHub.Business.Accounts.ContactRules.NameError);
        }
        if (!HoleInHub.Business.Accounts.ContactRules.IsPhone(request.Phone))
        {
            throw new ReservationValidationException(HoleInHub.Business.Accounts.ContactRules.PhoneError);
        }
        if (!request.PrivacyAccepted || request.Notes?.Length > 1000 || request.ResourceIds == null || request.ResourceIds.Length is < 1 or > 8 || request.ResourceIds.Distinct().Count() != request.ResourceIds.Length || request.ResourceIds.Any(string.IsNullOrWhiteSpace) || request.Participants == null || string.IsNullOrWhiteSpace(request.IdempotencyKey) || request.IdempotencyKey.Length > 100)
        {
            throw new ReservationValidationException("Complete your contact details, consent, and bay selection.");
        }

        if (request.Participants.Length > 0)
        {
            BookingRules.ValidateParticipants(request.Service, request.ResourceIds, request.Participants);
            request.Attendees = request.Participants.Sum(x => x.Players + x.NonPlayers);
        }
        else { BookingRules.ValidateGuests(request.Service, request.ResourceIds, request.Attendees); }
    }
    public async Task ValidateSubmissionAsync(SubmitReservationRequest request, bool demoAllowed, CancellationToken ct)
    {
        ValidateDetails(request, demoAllowed);
        var now = (clock ?? TimeProvider.System).GetUtcNow();
        policy.Value.ValidateCoverage(now, BookingRules.ReviewMinutes(request.Service));
        var (start, _) = ReservationRules.PreviewInterval(request.Date, request.StartTime, request.DurationMinutes, request.Service);
        BookingRules.ValidateWindow(request.Service, start, now);
        GetReviewDeadline(request.Service, start, now);
        var available = await GetAvailabilityAsync(request.Date, request.StartTime, request.DurationMinutes, request.Service, ct);
        if (request.ResourceIds.Any(id => !available.Any(x => x.Resource.Id == id && x.Available)) ||
            (request.Service == "WholeShop" && available.Count != request.ResourceIds.Length))
        {
            throw new ReservationValidationException("Your selected bays are unavailable. Choose another time or bay before submitting.");
        }
    }
    public async Task<ReservationContactDto?> GetLatestContactAsync(string accountId, CancellationToken ct)
    {
        var record = await persistence.LatestOwnedAsync(accountId, ct);
        return record == null ? null : new(record.FirstName, record.LastName, record.Phone);
    }
    public async Task<IReadOnlyList<ReservationDto>> GetOwnedAsync(string accountId, CancellationToken ct) => (await persistence.OwnedAsync(accountId, ct)).Select(Map).ToArray();
    public async Task<ReservationDto?> GetOwnedAsync(string accountId, string id, CancellationToken ct) { var record = await persistence.OwnedAsync(accountId, id, ct); return record == null ? null : Map(record); }
    public async Task ProcessPendingAsync(CancellationToken ct)
    {
        var now = (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime;
        await persistence.ExpireAsync(now, policy.Value.StaffEmails, ct);
        await persistence.MarkOverdueAsync(now, policy.Value.StaffEmails, ct);
        foreach (var record in await persistence.PendingAsync(now, ct))
        {
            var alerts = record.ReviewAlertsUtc ?? ((record.ReviewDeadlineUtc - record.CreatedUtc).TotalMinutes > 30 ? new[] { 30, 50 } : new[] { 15, 25 }).Select(minutes => record.CreatedUtc.AddMinutes(minutes)).ToArray();
            for (var index = 0; index < alerts.Length; index++)
            {
                var minutes = record.ReviewAlertsUtc is null ? (int)(alerts[index] - record.CreatedUtc).TotalMinutes
                    : record.Service == "Regular" ? new[] { 15, 25 }[index] : new[] { 30, 50 }[index];
                if (alerts[index] <= now && record.ReviewDeadlineUtc > now && (index == alerts.Length - 1 || now < alerts[index + 1]))
                {
                    foreach (var recipient in policy.Value.StaffEmails)
                    {
                        await outbox.EnqueueAsync(new OutboxRecord { Id = $"{record.Id}:review{minutes}:{recipient}", Recipient = recipient, Subject = $"Pending request: {record.Reference}", Body = $"Request {record.Reference} is awaiting review. Review deadline: {record.ReviewDeadlineUtc:O}.", DueUtc = now, ReservationId = record.Id, RequiredStatus = "Pending" }, ct);
                    }
                }
            }
        }
    }
    private DateTime GetReviewDeadline(string service, DateTime start, DateTimeOffset now)
    {
        var deadline = service == "WholeShop" ? now.UtcDateTime.AddMinutes(BookingRules.ReviewMinutes(service))
            : ReviewClock.AddMinutes(now, BookingRules.ReviewMinutes(service), policy.Value);
        if (start < deadline)
        {
            throw new ReservationValidationException("Choose a session after the staff review window finishes.");
        }
        return deadline;
    }
    public RentalQuoteDto GetDemoQuote(DateOnly date, TimeOnly time, int duration, bool demoAllowed)
    {
        if (!demoAllowed || !policy.Value.DemoPaymentsEnabled) { throw new ReservationValidationException("Checkout is unavailable."); }
        ReservationRules.PreviewInterval(date, time, duration, "WholeShop");
        var total = BookingRules.DemoRentalTotal(date, duration);
        return new RentalQuoteDto(total, total / 2);
    }
    public async Task<ReservationDto?> GetAccessAsync(string id, string hash, CancellationToken ct)
    {
        await ProcessPendingAsync(ct);
        var record = await persistence.ByAccessAsync(id, hash, ct);
        return record == null ? null : Map(record);
    }
    public async Task<IReadOnlyList<ReservationDto>> GetReviewQueueAsync(CancellationToken ct,
        string filter = "all", DateOnly? day = null, bool bySession = false)
    {
        await ProcessPendingAsync(ct);
        var now = (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime;
        DateTime? start = day.HasValue
            ? TimeZoneInfo.ConvertTimeToUtc(day.Value.ToDateTime(TimeOnly.MinValue), ReservationRules.Manila) : null;
        return (await persistence.ReviewQueueAsync(filter, now, start, bySession, ct)).Select(Map).ToArray();
    }

    public async Task<IReadOnlyList<ReservationDto>> GetExpiredRequestsAsync(DateOnly? day, CancellationToken ct)
    {
        if (day == DateOnly.MinValue) { throw new ReservationValidationException("Choose a valid session date."); }
        await ProcessPendingAsync(ct);
        DateTime? start = day.HasValue
            ? TimeZoneInfo.ConvertTimeToUtc(day.Value.ToDateTime(TimeOnly.MinValue), ReservationRules.Manila) : null;
        return (await persistence.ExpiredRequestsAsync(start, start?.AddDays(1), ct)).Select(Map).ToArray();
    }
    public async Task<ReservationDto?> GetStaffAsync(string id, CancellationToken ct)
    {
        await ProcessPendingAsync(ct);
        var record = await persistence.ByIdAsync(id, ct);
        return record == null ? null : Map(record);
    }
    public static bool IsReviewOverdue(ReservationDto booking, DateTime now) =>
        booking.Status == "Pending" && booking.Service != "WholeShop" && booking.ReviewDeadlineUtc <= now;

    public static bool CanCancelOnline(ReservationDto booking, DateTime now) =>
        (booking.Status is "Pending" or "Confirmed") && booking.StartUtc >= now.AddHours(1)
        && (booking.Service != "WholeShop" || !booking.CancellationRequested);

    public async Task<bool> ReviewAsync(string id, string action, string actor, CancellationToken ct, string? reason = null,
        long? expectedRevision = null, string? rebookUrl = null)
    {
        if (reason?.Length > 1000 || (action is "reject" or "keep" && string.IsNullOrWhiteSpace(reason)))
        {
            throw new ReservationValidationException("Enter a reason (up to 1,000 characters) for declining a booking or cancellation.");
        }
        try
        {
            return await persistence.TransitionAsync(id, action, actor, (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime, policy.Value.StaffEmails, ct, reason?.Trim() ?? "", expectedRevision, rebookUrl);
        }
        catch (AllocationConflictException error) { throw new ReservationValidationException(error.Message); }
    }
    public async Task<bool> DemoPaymentAsync(string id, string outcome, bool demoAllowed, CancellationToken ct)
    {
        if (!demoAllowed || !policy.Value.DemoPaymentsEnabled || outcome is not ("success" or "failure" or "cancel"))
        {
            throw new ReservationValidationException("Checkout is unavailable.");
        }

        return await persistence.ApplyDemoPaymentAsync(id, outcome, "DEMO-" + id, (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime, ct);
    }
    private static ReservationDto Map(ReservationRecord x) => new(x.Id, x.Reference, x.Service, x.Status, x.StartUtc, x.EndUtc, x.ReviewDeadlineUtc, x.ResourceIds, x.FirstName, x.LastName, x.Phone, x.Attendees, x.History.Select(h => new ReservationHistoryDto(h.Status, h.AtUtc, h.Actor, h.Reason)).ToArray(), x.Notes)
    {
        PolicyVersion = x.PolicyVersion,
        Revision = x.Revision,
        CheckedInUtc = x.CheckedInUtc,
        CheckedOutUtc = x.CheckedOutUtc,
        ResourceNames = x.ResourceNames,
        Participants = x.Participants.Select(p => new BayParticipants { ResourceId = p.ResourceId, Players = p.Players, NonPlayers = p.NonPlayers }).ToArray(),
        PaymentReference = x.PaymentReference,
        CreatedUtc = x.CreatedUtc,
        PaymentStatus = x.PaymentStatus,
        ScheduleChange = x.ScheduleChange is { } proposal ? new(proposal.Id, proposal.Status, proposal.RequestedUtc,
            proposal.DecidedUtc, proposal.StartUtc, proposal.EndUtc, proposal.Reason, proposal.ResourceIds, proposal.ResourceNames) : null,
        ReviewStartsUtc = x.ReviewStartsUtc,
        BookingSource = x.BookingSource,
        RentalTotal = x.RentalTotal,
        DepositAmount = x.DepositAmount,
        CoachConfirmed = x.CoachConfirmed,
        CancellationRequested = x.CancellationRequested
    };
}
