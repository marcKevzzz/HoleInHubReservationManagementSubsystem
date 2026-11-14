using System.Text.RegularExpressions;
using System.Security.Cryptography;
using System.Text;
using HoleInHub.Data.Reservations;

namespace HoleInHub.Business.Reservations;

public sealed record ResourceDirectoryEntry(string Id, string Name, string Service, bool Active,
    int MaxPlayers, int MaxTotalGuests, int? MaxNonPlayers, long Revision);
public sealed class ResourceCapacityWarningException(string message, string acknowledgement) : Exception(message)
{
    public string Acknowledgement { get; } = acknowledgement;
}

public sealed partial class ReservationService
{
    public async Task<IReadOnlyList<ResourceDirectoryEntry>> GetResourceDirectoryAsync(CancellationToken ct) =>
        (await persistence.ReadResourceDirectoryAsync(ct)).Select(x =>
        {
            var bay = MapResource(x);
            return new ResourceDirectoryEntry(x.Id, x.Name, x.Service, x.Active, bay.MaxPlayers, bay.MaxTotalGuests, bay.MaxNonPlayers, x.Revision);
        }).ToArray();

    public async Task AddBayAsync(string id, string name, string service, int? maxPlayers, int? maxTotalGuests,
        int? maxNonPlayers, CancellationToken ct)
    {
        id = id.Trim().ToLowerInvariant();
        name = name.Trim();
        if (id.Length is < 1 or > 32 || !Regex.IsMatch(id, "\\A[a-z0-9]+(?:-[a-z0-9]+)*\\z") ||
            name.Length is < 1 or > 80 || service is not ("Regular" or "VIP"))
        {
            throw new ReservationValidationException("Enter a bay name, a unique ID using letters, numbers or hyphens, and a bay type.");
        }
        var bay = BuildResource(id, name, service, maxPlayers, maxTotalGuests, maxNonPlayers);
        try { await persistence.AddResourceAsync(bay, ct); }
        catch (AllocationConflictException ex) { throw new ReservationValidationException(ex.Message); }
    }

    public async Task<bool> SetBayActiveAsync(string id, bool active, CancellationToken ct)
    {
        try { return await persistence.SetResourceActiveAsync(id, active, (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime, ct); }
        catch (AllocationConflictException ex) { throw new ReservationValidationException(ex.Message); }
    }
    public async Task<bool> EditBayAsync(string id, string name, string service, int? maxPlayers, int? maxTotalGuests,
        int? maxNonPlayers, long? expectedRevision, string? acknowledgement, CancellationToken ct)
    {
        name = name.Trim();
        if (string.IsNullOrWhiteSpace(id) || id.Length > 32 || name.Length is < 1 or > 80 || service is not ("Regular" or "VIP"))
        { throw new ReservationValidationException("Enter a bay name and choose Regular or VIP."); }
        if (expectedRevision is null or < 0)
        { throw new ReservationValidationException("Reload the inventory before editing this bay."); }
        var bay = BuildResource(id, name, service, maxPlayers, maxTotalGuests, maxNonPlayers);
        try
        {
            return await persistence.EditResourceAsync(bay, expectedRevision.Value, (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime, ct,
                (current, edited, catalog, upcoming) => VerifyCapacityChange(current, edited, catalog, upcoming, acknowledgement));
        }
        catch (AllocationConflictException ex) { throw new ReservationValidationException(ex.Message); }
    }

    private static ResourceRecord BuildResource(string id, string name, string service, int? maxPlayers, int? maxTotalGuests, int? maxNonPlayers)
    {
        var bay = new ResourceRecord { Id = id, Name = name, Service = service, MaxPlayers = maxPlayers,
            MaxTotalGuests = maxTotalGuests, MaxNonPlayers = maxNonPlayers };
        var limits = MapResource(bay);
        if (limits.MaxPlayers < 1 || limits.MaxTotalGuests < limits.MaxPlayers || maxNonPlayers < 0 || maxNonPlayers > limits.MaxTotalGuests)
        {
            throw new ReservationValidationException("Maximum players and total guests must be positive. The player and non-player limits must fit within the total guest limit.");
        }
        return bay;
    }

    private static ResourceDto MapResource(ResourceRecord bay) => new(bay.Id, bay.Name, bay.Service)
    {
        MaxPlayers = bay.MaxPlayers ?? 6,
        MaxTotalGuests = bay.MaxTotalGuests ?? (bay.Service == "VIP" ? 10 : 8),
        MaxNonPlayers = bay.MaxNonPlayers
    };

    private async Task<IReadOnlyList<ResourceDto>> ResolveSelectedBaysAsync(string[] resources, CancellationToken ct)
    {
        var bays = (await GetCatalogAsync(ct)).Where(bay => resources.Contains(bay.Id)).ToArray();
        if (bays.Length != resources.Length)
        { throw new ReservationValidationException("One or more selected resources are unavailable for this service."); }
        return bays;
    }

    private static void ValidateBookingCapacity(string service, IReadOnlyList<ResourceDto> bays, int attendees, BayParticipants[] participants,
        bool allowEmptyBays = false)
    {
        BookingRules.ValidateGuests(service, bays, attendees);
        if (participants.Length > 0) { BookingRules.ValidateParticipants(service, bays, participants, allowEmptyBays); }
    }

    private static void ValidateStoredCapacity(ReservationRecord record, IReadOnlyList<ResourceRecord> selected) =>
        ValidateBookingCapacity(record.Service, selected.Select(MapResource).ToArray(), record.Attendees,
            record.Participants.Select(p => new BayParticipants { ResourceId = p.ResourceId, Players = p.Players, NonPlayers = p.NonPlayers }).ToArray(),
            allowEmptyBays: record.PolicyVersion != 1);

    private static ParticipantRecord[] ConfirmParticipants(ReservationRecord record, IReadOnlyList<ResourceRecord> selected,
        BayParticipants[]? participants)
    {
        if ((participants is null || participants.Length == 0) && record is { Attendees: 1, ResourceIds.Length: 1 })
        { participants = [new() { ResourceId = record.ResourceIds[0], Players = 1 }]; }
        if (participants is null || participants.Length == 0 || participants.Sum(x => (long)x.Players + x.NonPlayers) != record.Attendees)
        { throw new ReservationValidationException($"Enter a breakdown matching the {record.Attendees} guests on this request."); }

        ValidateBookingCapacity(record.Service, selected.Select(MapResource).ToArray(), record.Attendees, participants, allowEmptyBays: true);
        return participants.Select(x => new ParticipantRecord { ResourceId = x.ResourceId, Players = x.Players, NonPlayers = x.NonPlayers }).ToArray();
    }

    private static void VerifyCapacityChange(ResourceRecord current, ResourceRecord edited, IReadOnlyList<ResourceRecord> catalog,
        IReadOnlyList<ReservationRecord> upcoming, string? acknowledgement)
    {
        var before = MapResource(current);
        var after = MapResource(edited);
        if (after.MaxPlayers >= before.MaxPlayers && after.MaxTotalGuests >= before.MaxTotalGuests &&
            (after.MaxNonPlayers ?? int.MaxValue) >= (before.MaxNonPlayers ?? int.MaxValue)) { return; }

        var capacity = catalog.Select(x => x.Id == edited.Id ? edited : x).ToArray();
        var affected = new List<string>();
        foreach (var booking in upcoming)
        {
            try { ValidateStoredCapacity(booking, capacity.Where(x => booking.ResourceIds.Contains(x.Id)).ToArray()); }
            catch (ReservationValidationException) { affected.Add(booking.Reference); }
        }
        if (affected.Count == 0) { return; }
        var references = string.Join(", ", affected.Order(StringComparer.Ordinal));
        var challenge = $"{edited.Id}:{current.Revision}:{edited.MaxPlayers}:{edited.MaxTotalGuests}:{edited.MaxNonPlayers}:{references}";
        var expected = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(challenge)));
        if (acknowledgement != expected)
        {
            throw new ResourceCapacityWarningException($"The new capacity would affect {references}. Review these bookings before saving the new limits.", expected);
        }
    }
}
