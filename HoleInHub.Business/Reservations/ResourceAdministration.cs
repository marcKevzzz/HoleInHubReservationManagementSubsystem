using System.Text.RegularExpressions;
using HoleInHub.Data.Reservations;

namespace HoleInHub.Business.Reservations;

public sealed record ResourceDirectoryEntry(string Id, string Name, string Service, bool Active);

public sealed partial class ReservationService
{
    public async Task<IReadOnlyList<ResourceDirectoryEntry>> GetResourceDirectoryAsync(CancellationToken ct) =>
        (await persistence.ReadResourceDirectoryAsync(ct)).Select(x => new ResourceDirectoryEntry(x.Id, x.Name, x.Service, x.Active)).ToArray();

    public async Task AddBayAsync(string id, string name, string service, CancellationToken ct)
    {
        id = id.Trim().ToLowerInvariant();
        name = name.Trim();
        if (id.Length is < 1 or > 32 || !Regex.IsMatch(id, "\\A[a-z0-9]+(?:-[a-z0-9]+)*\\z") ||
            name.Length is < 1 or > 80 || service is not ("Regular" or "VIP"))
        {
            throw new ReservationValidationException("Enter a bay name, a unique ID using letters, numbers or hyphens, and a bay type.");
        }
        try { await persistence.AddResourceAsync(new ResourceRecord { Id = id, Name = name, Service = service }, ct); }
        catch (AllocationConflictException ex) { throw new ReservationValidationException(ex.Message); }
    }

    public async Task<bool> SetBayActiveAsync(string id, bool active, CancellationToken ct)
    {
        try { return await persistence.SetResourceActiveAsync(id, active, (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime, ct); }
        catch (AllocationConflictException ex) { throw new ReservationValidationException(ex.Message); }
    }
}
