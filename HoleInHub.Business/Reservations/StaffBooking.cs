using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using HoleInHub.Business.Accounts;
using HoleInHub.Data.Customers;
using HoleInHub.Data.Reservations;

namespace HoleInHub.Business.Reservations;

public sealed record StaffBookingRequest(string ResourceId, DateOnly Date, TimeOnly StartTime,
    int DurationMinutes, string FirstName, string LastName, string Phone, int Attendees,
    string Source, string IdempotencyKey)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CustomerProfileId { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string[]? AdditionalResourceIds { get; init; }
}

public sealed partial class ReservationService
{
    public async Task<ReservationDto> CreateStaffBookingAsync(StaffBookingRequest request, string actor, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(actor) || string.IsNullOrWhiteSpace(request.IdempotencyKey)
            || request.IdempotencyKey.Length > 100 || string.IsNullOrWhiteSpace(request.ResourceId)
            || request.Source is not ("Walk-in" or "Phone" or "Messaging"))
        {
            throw new ReservationValidationException("Choose a bay and booking source before confirming.");
        }

        if (!ContactRules.IsName(request.FirstName) || !ContactRules.IsName(request.LastName))
        {
            throw new ReservationValidationException(ContactRules.NameError);
        }
        if (!ContactRules.IsPhone(request.Phone?.Trim()))
        {
            throw new ReservationValidationException(ContactRules.PhoneError);
        }

        if (request.CustomerProfileId is not null && (request.CustomerProfileId.Length != 67
            || !request.CustomerProfileId.StartsWith("cp_", StringComparison.Ordinal) || !request.CustomerProfileId[3..].All(char.IsAsciiHexDigit)))
        { throw new ReservationValidationException("Choose a valid customer profile from the customer directory."); }
        request = request with { FirstName = request.FirstName.Trim(), LastName = request.LastName.Trim(), Phone = request.Phone!.Trim() };
        var resources = new[] { request.ResourceId }.Concat(request.AdditionalResourceIds ?? []).ToArray();
        if (resources.Length > 8 || resources.Any(string.IsNullOrWhiteSpace)
            || resources.Distinct(StringComparer.Ordinal).Count() != resources.Length)
        {
            throw new ReservationValidationException("Choose up to eight different bays.");
        }
        Array.Sort(resources, StringComparer.Ordinal);
        request = request with { ResourceId = resources[0], AdditionalResourceIds = resources.Length > 1 ? resources[1..] : null };
        var scope = "staff:" + actor;
        var hash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(request)));
        ReservationDto Result(ReservationRecord saved)
        {
            if (saved.SubmissionHash != hash)
            {
                throw new ReservationValidationException("This form already confirmed a different booking. Open New booking to start another.");
            }
            return Map(saved);
        }

        var prior = await persistence.ByKeyAsync(scope, request.IdempotencyKey, ct);
        if (prior is not null)
        {
            return Result(prior);
        }

        var bays = (await GetCatalogAsync(ct)).Where(bay => resources.Contains(bay.Id)).ToArray();
        if (bays.Length != resources.Length || bays.Any(bay => bay.Service is not ("Regular" or "VIP") || bay.Service != bays[0].Service))
        {
            throw new ReservationValidationException("Choose active bays of the same type: Regular or VIP.");
        }
        var service = bays[0].Service;
        BookingRules.ValidateGuests(service, bays, request.Attendees);
        var entry = await ResolveBookingServiceAsync(service, null, ct, existingBooking: true);
        ValidateServiceCapacity(entry, request.Attendees);
        var (start, end) = PreviewServiceInterval(entry, request.Date, request.StartTime, request.DurationMinutes, service);
        var now = (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime;
        // The current half-hour is available for immediate arrivals.
        if (start <= now.AddMinutes(-30))
        {
            throw new ReservationValidationException("Choose the current half-hour or a future time.");
        }

        await ProcessPendingAsync(ct);
        var record = new ReservationRecord
        {
            AccountId = scope,
            Reference = $"HIH-{Guid.NewGuid().ToString("N")[..10].ToUpperInvariant()}",
            BookingSource = request.Source,
            Status = "Confirmed",
            PolicyVersion = 2,
            Service = service,
            ServiceEntryId = entry?.Active == true ? entry.Id : null,
            ServiceRevision = entry?.Active == true ? entry.Revision : null,
            RentalTotal = PriceBooking(entry, service, request.Date, request.StartTime, request.DurationMinutes, resources.Length).Total,
            StartUtc = start,
            EndUtc = end,
            CreatedUtc = now,
            ReviewDeadlineUtc = now,
            ResourceIds = resources,
            FirstName = request.FirstName,
            LastName = request.LastName,
            Phone = request.Phone,
            Attendees = request.Attendees,
            IdempotencyKey = request.IdempotencyKey,
            SubmissionHash = hash,
            History = [new HistoryRecord
            {
                EventType = "ReservationConfirmed", ReservationStatus = "Confirmed", Status = "Confirmed",
                AtUtc = now, Actor = actor, Reason = "Staff-assisted booking."
            }]
        };
        try { return Result(await persistence.InsertAsync(record, ct, customerProfileId: request.CustomerProfileId, validateCapacity: ValidateStoredCapacity)); }
        catch (CustomerProfileConflictException e) { throw new ReservationValidationException(e.Message); }
        catch (AllocationConflictException error) { throw new ReservationValidationException(error.Message); }
    }
}
