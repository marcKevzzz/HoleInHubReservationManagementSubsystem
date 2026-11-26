using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using HoleInHub.Business.Services;
using HoleInHub.Data.Reservations;
using HoleInHub.Data.Services;

namespace HoleInHub.Business.Reservations;

public sealed record ResourceDirectoryEntry(string Id, string Name, string Service, bool Active,
    int MaxPlayers, int MaxTotalGuests, int? MaxNonPlayers, long Revision)
{
    public bool HasSavedPresentation { get; init; }
    public string Location { get; init; } = "";
    public string Description { get; init; } = "";
    public string[] Images { get; init; } = [];
}

public sealed record BayPresentationEdit(string Location, string Description, string[] Images, string[] ImageOrder, IReadOnlyList<byte[]> Uploads)
{
    public string[] LegacyImages { get; init; } = [];
}
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
            return new ResourceDirectoryEntry(x.Id, x.Name, x.Service, x.Active, bay.MaxPlayers, bay.MaxTotalGuests, bay.MaxNonPlayers, x.Revision)
            { HasSavedPresentation = bay.HasSavedPresentation, Location = bay.Location, Description = bay.Description, Images = bay.Images };
        }).ToArray();

    public async Task AddBayAsync(string id, string name, string service, int? maxPlayers, int? maxTotalGuests,
        int? maxNonPlayers, CancellationToken ct, BayPresentationEdit? presentation = null, bool active = true)
    {
        id = id.Trim().ToLowerInvariant();
        name = name.Trim();
        if (id.Length is < 1 or > 32 || !Regex.IsMatch(id, "\\A[a-z0-9]+(?:-[a-z0-9]+)*\\z") ||
            name.Length is < 1 or > 80 || service is not ("Regular" or "VIP"))
        {
            throw new ReservationValidationException("Enter a bay name, a unique ID using letters, numbers or hyphens, and a bay type.");
        }
        var bay = BuildResource(id, name, service, maxPlayers, maxTotalGuests, maxNonPlayers);
        bay.Active = active;
        var images = PrepareBayGallery(bay, presentation, []);
        try { await persistence.AddResourceAsync(bay, ct, images); }
        catch (AllocationConflictException ex) { throw new ReservationValidationException(ex.Message); }
    }

    public async Task<bool> SetBayActiveAsync(string id, bool active, CancellationToken ct)
    {
        try { return await persistence.SetResourceActiveAsync(id, active, (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime, ct); }
        catch (AllocationConflictException ex) { throw new ReservationValidationException(ex.Message); }
    }
    public async Task<bool> EditBayAsync(string id, string name, string service, int? maxPlayers, int? maxTotalGuests,
        int? maxNonPlayers, long? expectedRevision, string? acknowledgement, CancellationToken ct, BayPresentationEdit? presentation = null, bool? active = null)
    {
        name = name.Trim();
        if (string.IsNullOrWhiteSpace(id) || id.Length > 32 || name.Length is < 1 or > 80 || service is not ("Regular" or "VIP"))
        { throw new ReservationValidationException("Enter a bay name and choose Regular or VIP."); }
        if (expectedRevision is null or < 0)
        { throw new ReservationValidationException("Reload the inventory before editing this bay."); }
        var bay = BuildResource(id, name, service, maxPlayers, maxTotalGuests, maxNonPlayers);
        var prior = presentation is null ? null : await persistence.GetResourceAsync(id, ct);
        if (presentation is not null && prior is null) { return false; }
        var images = PrepareBayGallery(bay, presentation, prior?.Images ?? presentation?.LegacyImages ?? []);
        try
        {
            return await persistence.EditResourceAsync(bay, expectedRevision.Value, (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime, ct,
                (current, edited, catalog, upcoming) => VerifyCapacityChange(current, edited, catalog, upcoming, acknowledgement), images, active);
        }
        catch (AllocationConflictException ex) { throw new ReservationValidationException(ex.Message); }
    }

    private static ServiceImageRecord[] PrepareBayGallery(ResourceRecord bay, BayPresentationEdit? edit, string[] existing)
    {
        if (edit is null) { return []; }
        bay.Location = edit.Location.Trim();
        bay.Description = edit.Description.Trim();
        if (bay.Location.Length > 100 || bay.Description.Length > 500)
        { throw new ReservationValidationException("Use up to 100 characters for the location and 500 for the description."); }
        if (edit.Images.Length + edit.Uploads.Count > 8 || edit.Uploads.Sum(bytes => (long)bytes.Length) > 20 * 1024 * 1024)
        { throw new ReservationValidationException("Choose up to eight photos and upload up to 20 MB per save."); }
        if (edit.Images.Any(path => !existing.Contains(path)) || edit.Images.Distinct().Count() != edit.Images.Length)
        { throw new ReservationValidationException("Reload the current bay photos and try again."); }
        try
        {
            var (images, paths) = ServiceCatalog.PrepareGallery("bay:" + bay.Id, edit.Images, edit.ImageOrder, edit.Uploads);
            bay.Images = paths;
            return images;
        }
        catch (ArgumentException ex) { throw new ReservationValidationException(ex.Message); }
    }

    private static ResourceRecord BuildResource(string id, string name, string service, int? maxPlayers, int? maxTotalGuests, int? maxNonPlayers)
    {
        var bay = new ResourceRecord
        {
            Id = id,
            Name = name,
            Service = service,
            MaxPlayers = maxPlayers,
            MaxTotalGuests = maxTotalGuests,
            MaxNonPlayers = maxNonPlayers
        };
        var limits = MapResource(bay);
        if (limits.MaxPlayers < 1 || limits.MaxTotalGuests < limits.MaxPlayers || maxNonPlayers < 0 || maxNonPlayers > limits.MaxTotalGuests)
        {
            throw new ReservationValidationException("Maximum players and total guests must be positive. The player and non-player limits must fit within the total guest limit.");
        }
        return bay;
    }

    private static ResourceDto MapResource(ResourceRecord bay) => new(bay.Id, bay.Name, bay.Service)
    {
        HasSavedPresentation = bay.Images is not null,
        Location = bay.Location ?? bay.Id switch
        {
            "bay-1" => "Front row · Left",
            "bay-2" => "Front row · Centre",
            "bay-3" => "Front row · Right",
            "bay-4" => "Rear row · Left",
            "bay-5" => "Rear row · Centre",
            "bay-6" => "Rear row · Right",
            "vip-1" or "vip-7" => "VIP area · Left",
            "vip-2" or "vip-8" => "VIP area · Right",
            _ => ""
        },
        Description = bay.Description ?? (bay.Service == "VIP"
            ? "A private simulator bay for a focused practice session or a round with your group."
            : "An open simulator bay for solo practice or a shared round with friends."),
        Images = bay.Images ?? [],
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

    private static void ValidateStoredCapacity(ReservationRecord record, IReadOnlyList<ResourceRecord> selected)
    {
        if (record.NonPlayerGuests is int visitors && record.Participants.Length == 0)
        {
            if (visitors < 0 || visitors > record.Attendees) { throw new ReservationValidationException("Include non-playing guests in the total guest count.", "NonPlayerGuests"); }
            var players = record.Attendees - visitors;
            var bays = selected.Select(MapResource).ToArray();
            var rows = bays.Select(b => new ParticipantRecord { ResourceId = b.Id }).ToArray();
            for (var i = 0; i < bays.Length; i++)
            {
                rows[i].Players = Math.Min(players, Math.Min(bays[i].MaxPlayers, bays[i].MaxTotalGuests - (bays[i].MaxNonPlayers ?? bays[i].MaxTotalGuests)));
                players -= rows[i].Players;
            }
            for (var i = 0; i < bays.Length; i++)
            {
                var extra = Math.Min(players, bays[i].MaxPlayers - rows[i].Players);
                rows[i].Players += extra;
                players -= extra;
                rows[i].NonPlayers = Math.Min(visitors, Math.Min(bays[i].MaxTotalGuests - rows[i].Players, bays[i].MaxNonPlayers ?? bays[i].MaxTotalGuests));
                visitors -= rows[i].NonPlayers;
            }
            if (players != 0 || visitors != 0) { throw new ReservationValidationException("Check the number of non-playing guests for the selected bays.", "NonPlayerGuests"); }
            record.Participants = rows;
        }
        ValidateBookingCapacity(record.Service, selected.Select(MapResource).ToArray(), record.Attendees,
            record.Participants.Select(p => new BayParticipants { ResourceId = p.ResourceId, Players = p.Players, NonPlayers = p.NonPlayers }).ToArray(),
            allowEmptyBays: record.PolicyVersion != 1 || record.NonPlayerGuests.HasValue);
    }

    private static ParticipantRecord[] ConfirmParticipants(ReservationRecord record, IReadOnlyList<ResourceRecord> selected,
        BayParticipants[]? participants, DateTime now)
    {
        var bays = selected.Select(MapResource).OrderBy(bay => bay.Id, StringComparer.Ordinal).ToArray();
        if (participants is null || participants.Length == 0)
        {
            participants = BookingRules.AssignOverdueParticipants(bays, record.Attendees);
        }
        if ((participants is null || participants.Length == 0) && record is { Attendees: 1, ResourceIds.Length: 1 })
        { participants = [new() { ResourceId = record.ResourceIds[0], Players = 1 }]; }
        if (participants is null || participants.Length == 0 || participants.Sum(x => (long)x.Players + x.NonPlayers) != record.Attendees)
        { throw new ReservationValidationException($"Enter a breakdown matching the {record.Attendees} guests on this request."); }

        ValidateBookingCapacity(record.Service, bays, record.Attendees, participants, allowEmptyBays: true);
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
