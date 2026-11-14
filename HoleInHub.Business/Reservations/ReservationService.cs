using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HoleInHub.Data.Reservations;
using HoleInHub.Data.Services;
using Microsoft.Extensions.Options;
namespace HoleInHub.Business.Reservations;

public sealed partial class ReservationService(ReservationPersistence persistence, OutboxPersistence outbox, IOptions<ReservationPolicy> policy, TimeProvider? clock = null, HoleInHub.Business.Settings.OperationalSettings? settings = null, ServiceCatalogPersistence? serviceCatalog = null)
{
    private ReservationPolicy? runtimePolicy;
    private ReservationPolicy effectivePolicy => runtimePolicy ?? policy.Value;
    private async Task LoadOperationalPolicyAsync(CancellationToken ct)
    {
        if (settings is not null) { runtimePolicy = await settings.PolicyAsync(ct); }
    }
    public async Task<IReadOnlyList<ResourceDto>> GetCatalogAsync(CancellationToken ct) => (await persistence.CatalogAsync(ct)).Select(MapResource).ToArray();
    public async Task<IReadOnlyList<AvailabilityDto>> GetAvailabilityAsync(DateOnly date, TimeOnly startTime, int durationMinutes,
        string service, CancellationToken ct, string? serviceEntryId = null)
    {
        var (start, end) = await PreviewBookingIntervalAsync(date, startTime, durationMinutes, service, ct, serviceEntryId);
        var now = (clock ?? TimeProvider.System).GetUtcNow();
        BookingRules.ValidateLeadTime(service, start, now);
        await ProcessPendingAsync(ct);
        var occupied = await persistence.OccupiedAsync(start, end, ct, now.UtcDateTime);
        return (await GetCatalogAsync(ct)).Where(bay => service == "WholeShop" || bay.Service == BookingRules.ResourceService(service))
            .Select(bay => new AvailabilityDto(bay, !occupied.Contains(bay.Id))).ToArray();
    }
    public async Task<ReservationDto> SubmitAsync(SubmitReservationRequest request, string accountId, string verifiedEmail, CancellationToken ct, string? guestSessionHash = null, string? accessTokenHash = null, bool demoAllowed = false, string? encryptedReceipt = null)
    {
        if (string.IsNullOrWhiteSpace(accountId) || !System.Net.Mail.MailAddress.TryCreate(verifiedEmail, out var address) || address.Address != verifiedEmail)
        {
            throw new ReservationValidationException("Verify your email before submitting a request.");
        }

        await LoadOperationalPolicyAsync(ct);
        ValidateDetails(request, demoAllowed);
        var hash = Fingerprint(request, verifiedEmail);
        var prior = await persistence.ByKeyAsync(accountId, request.IdempotencyKey, ct); if (prior != null) { if (prior.SubmissionHash != hash) { throw new ReservationValidationException("This submission key was already used for another request. Reload the form."); } return Map(prior); }
        var entry = await ResolveBookingServiceAsync(request.Service, request.ServiceEntryId, ct);
        ValidateServiceCapacity(entry, request.Attendees);
        var now = (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime;
        effectivePolicy.ValidateCoverage(new DateTimeOffset(now), BookingRules.ReviewMinutes(request.Service));
        var (start, end) = PreviewServiceInterval(entry, request.Date, request.StartTime, request.DurationMinutes, request.Service);
        var quote = ValidateQuote(entry, request);
        BookingRules.ValidateWindow(request.Service, start, new DateTimeOffset(now));
        if (start <= now)
        {
            throw new ReservationValidationException("Choose a future reservation time.");
        }

        await ProcessPendingAsync(ct);
        var reviewDeadline = GetReviewDeadline(request.Service, start, new DateTimeOffset(now));
        var reviewStart = request.Service == "WholeShop" ? (DateTime?)null : ReviewClock.AddMinutes(new DateTimeOffset(now), 0, effectivePolicy);
        var alertMinutes = request.Service == "Regular" ? new[] { 15, 25 } : new[] { 30, 50 };
        var record = new ReservationRecord
        {
            PolicyVersion = request.Participants.Length == 0 ? 2 : 1,
            EmailVerificationId = guestSessionHash,
            AccessTokenHash = accessTokenHash,
            Participants = request.Participants.Select(x => new ParticipantRecord { ResourceId = x.ResourceId, Players = x.Players, NonPlayers = x.NonPlayers }).ToArray(),
            RentalTotal = quote.Total,
            DepositAmount = quote.DepositAmount > 0 ? quote.DepositAmount : null,
            PaymentStatus = request.Service == "WholeShop" ? "AwaitingDeposit" : "NotRequired",
            AccountId = accountId,
            Email = verifiedEmail,
            Reference = $"HIH-{Guid.NewGuid().ToString("N")[..10].ToUpperInvariant()}",
            Service = request.Service,
            ServiceEntryId = entry?.Id ?? request.ServiceEntryId,
            ServiceRevision = entry?.Revision,
            StartUtc = start,
            EndUtc = end,
            CreatedUtc = now,
            ReviewDeadlineUtc = reviewDeadline,
            ReviewStartsUtc = reviewStart,
            ReviewAlertsUtc = reviewStart.HasValue ? alertMinutes.Select(minutes => ReviewClock.AddMinutes(new DateTimeOffset(now), minutes, effectivePolicy)).ToArray() : null,
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
        try { var saved = await persistence.InsertAsync(record, ct, encryptedReceipt, validateCapacity: ValidateStoredCapacity); if (saved.SubmissionHash != hash) { throw new ReservationValidationException("This submission key was already used for another request."); } return Map(saved); } catch (AllocationConflictException e) { throw new ReservationValidationException(e.Message); }
    }
    private static string Fingerprint(SubmitReservationRequest request, string email)
    {
        request.Email = email;
        request.Participants = request.Participants.OrderBy(x => x.ResourceId).ToArray();
        request.ResourceIds = request.ResourceIds.Order().ToArray();
        var quoteKey = request.QuoteKey;
        request.QuoteKey = null;
        try { return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request)))); }
        finally { request.QuoteKey = quoteKey; }
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

        if (request.Service == "WholeShop" && (!demoAllowed || !effectivePolicy.DemoPaymentsEnabled))
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
            var attendees = request.Participants.Sum(x => (long)x.Players + x.NonPlayers);
            if (attendees is < 1 or > int.MaxValue) { throw new ReservationValidationException("Enter valid guest counts."); }
            request.Attendees = (int)attendees;
        }
        else if (request.Attendees < 1) { throw new ReservationValidationException("Enter at least one guest."); }
    }
    public async Task ValidateSubmissionAsync(SubmitReservationRequest request, bool demoAllowed, CancellationToken ct)
    {
        await LoadOperationalPolicyAsync(ct);
        ValidateDetails(request, demoAllowed);
        var now = (clock ?? TimeProvider.System).GetUtcNow();
        effectivePolicy.ValidateCoverage(now, BookingRules.ReviewMinutes(request.Service));
        var entry = await ResolveBookingServiceAsync(request.Service, request.ServiceEntryId, ct);
        ValidateServiceCapacity(entry, request.Attendees);
        var (start, _) = PreviewServiceInterval(entry, request.Date, request.StartTime, request.DurationMinutes, request.Service);
        ValidateQuote(entry, request);
        BookingRules.ValidateWindow(request.Service, start, now);
        GetReviewDeadline(request.Service, start, now);
        var available = await GetAvailabilityAsync(request.Date, request.StartTime, request.DurationMinutes, request.Service, ct, request.ServiceEntryId);
        if (request.ResourceIds.Any(id => !available.Any(x => x.Resource.Id == id && x.Available)) ||
            (request.Service == "WholeShop" && available.Count != request.ResourceIds.Length))
        {
            throw new ReservationValidationException("Your selected bays are unavailable. Choose another time or bay before submitting.");
        }
        ValidateBookingCapacity(request.Service, available.Where(bay => request.ResourceIds.Contains(bay.Resource.Id))
            .Select(bay => bay.Resource).ToArray(), request.Attendees, request.Participants);
    }
    private static void ValidateServiceCapacity(ServiceCatalogRecord? entry, int attendees)
    {
        if (entry?.MaxPax is int capacity && attendees > capacity)
        { throw new ReservationValidationException($"This service allows up to {capacity} guests per booking."); }
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
        await LoadOperationalPolicyAsync(ct);
        var now = (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime;
        await persistence.ExpireAsync(now, effectivePolicy.StaffEmails, ct);
        await persistence.MarkOverdueAsync(now, effectivePolicy.StaffEmails, ct);
        foreach (var reservation in await persistence.UpcomingRemindersAsync(now, ct))
        {
            if (ReservationReminderPolicy.Create(reservation, now) is { } reminder)
            {
                await outbox.EnqueueAsync(reminder, ct);
            }
        }
        foreach (var record in await persistence.PendingAsync(now, ct))
        {
            var alerts = record.ReviewAlertsUtc ?? ((record.ReviewDeadlineUtc - record.CreatedUtc).TotalMinutes > 30 ? new[] { 30, 50 } : new[] { 15, 25 }).Select(minutes => record.CreatedUtc.AddMinutes(minutes)).ToArray();
            for (var index = 0; index < alerts.Length; index++)
            {
                var minutes = record.ReviewAlertsUtc is null ? (int)(alerts[index] - record.CreatedUtc).TotalMinutes
                    : record.Service == "Regular" ? new[] { 15, 25 }[index] : new[] { 30, 50 }[index];
                if (alerts[index] <= now && record.ReviewDeadlineUtc > now && (index == alerts.Length - 1 || now < alerts[index + 1]))
                {
                    foreach (var recipient in effectivePolicy.StaffEmails)
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
            : ReviewClock.AddMinutes(now, BookingRules.ReviewMinutes(service), effectivePolicy);
        if (start < deadline)
        {
            throw new ReservationValidationException("Choose a session after the staff review window finishes.");
        }
        return deadline;
    }
    public RentalQuoteDto GetDemoQuote(DateOnly date, TimeOnly time, int duration, bool demoAllowed)
    {
        if (!demoAllowed || !effectivePolicy.DemoPaymentsEnabled) { throw new ReservationValidationException("Checkout is unavailable."); }
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
        string filter = "all", DateOnly? day = null, bool bySession = false, int page = 0)
    {
        await ProcessPendingAsync(ct);
        var now = (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime;
        DateTime? start = day.HasValue
            ? TimeZoneInfo.ConvertTimeToUtc(day.Value.ToDateTime(TimeOnly.MinValue), ReservationRules.Manila) : null;
        return (await persistence.ReviewQueueAsync(filter, now, start, bySession, ct, page)).Select(Map).ToArray();
    }

    public async Task<IReadOnlyList<ReservationDto>> GetExpiredRequestsAsync(DateOnly? day, CancellationToken ct, int page = 0, bool bySession = false)
    {
        if (day == DateOnly.MinValue) { throw new ReservationValidationException("Choose a valid session date."); }
        await ProcessPendingAsync(ct);
        DateTime? start = day.HasValue
            ? TimeZoneInfo.ConvertTimeToUtc(day.Value.ToDateTime(TimeOnly.MinValue), ReservationRules.Manila) : null;
        return (await persistence.ExpiredRequestsAsync(start, start?.AddDays(1), ct, page, bySession)).Select(Map).ToArray();
    }
    public Task<bool> StaffRecordExistsAsync(string id, CancellationToken ct) => persistence.ExistsAsync(id, ct);

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
        long? expectedRevision = null, string? rebookUrl = null, BayParticipants[]? participants = null)
    {
        if (reason?.Length > 1000 || (action is "reject" or "keep" && string.IsNullOrWhiteSpace(reason)))
        {
            throw new ReservationValidationException("Enter a reason (up to 1,000 characters) for declining a booking or cancellation.");
        }
        try
        {
            await LoadOperationalPolicyAsync(ct);
            return await persistence.TransitionAsync(id, action, actor, (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime, effectivePolicy.StaffEmails, ct, reason?.Trim() ?? "", expectedRevision, rebookUrl,
                confirmParticipants: (record, selected) => ConfirmParticipants(record, selected, participants));
        }
        catch (AllocationConflictException error) { throw new ReservationValidationException(error.Message); }
    }
    public async Task<bool> DemoPaymentAsync(string id, string outcome, bool demoAllowed, CancellationToken ct)
    {
        if (!demoAllowed || !effectivePolicy.DemoPaymentsEnabled || outcome is not ("success" or "failure" or "cancel"))
        {
            throw new ReservationValidationException("Checkout is unavailable.");
        }

        return await persistence.ApplyDemoPaymentAsync(id, outcome, "DEMO-" + id, (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime, ct);
    }
    private static ReservationDto Map(ReservationRecord x) => new(x.Id, x.Reference, x.Service, x.Status, x.StartUtc, x.EndUtc, x.ReviewDeadlineUtc, x.ResourceIds, x.FirstName, x.LastName, x.Phone, x.Attendees, x.History.Select(h => new ReservationHistoryDto(h.Status, h.AtUtc, h.Actor, h.Reason)).ToArray(), x.Notes)
    {
        PolicyVersion = x.PolicyVersion,
        ServiceEntryId = x.ServiceEntryId,
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
