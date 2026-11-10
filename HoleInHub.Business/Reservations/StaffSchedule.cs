namespace HoleInHub.Business.Reservations;

public sealed record StaffScheduleBay(string Name, bool Active, IReadOnlyList<StaffScheduleEntry> Entries, string Service = "Regular", string Id = "");
public sealed record StaffScheduleEntry(DateTime Start, DateTime End, string Status,
    string? ReservationId = null, string? Reference = null, string? Service = null, string? Guest = null, int Attendees = 0, string? Reason = null, string? BlockId = null, DateTime? CheckedOut = null,
    long Revision = 0, bool CanMove = false, string Location = "")
{
    public bool HoldsCapacity => Status is "Pending" or "Confirmed" or "Checked-In" or "Blocked";
}

public sealed partial class ReservationService
{
    public Task<string?> FindStaffBookingAsync(string reference, CancellationToken ct) =>
        persistence.FindIdByReferenceAsync(reference, ct);

    public async Task<IReadOnlyList<StaffScheduleEntry>> SearchStaffBookingsAsync(string term, CancellationToken ct) =>
        (await persistence.SearchStaffBookingsAsync(term, ct)).Select(ScheduleEntry).ToArray();

    public async Task<IReadOnlyList<StaffScheduleBay>> GetStaffScheduleAsync(DateOnly day, CancellationToken ct)
    {
        var start = TimeZoneInfo.ConvertTimeToUtc(day.ToDateTime(TimeOnly.MinValue), ReservationRules.Manila);
        var end = TimeZoneInfo.ConvertTimeToUtc(day.AddDays(1).ToDateTime(TimeOnly.MinValue), ReservationRules.Manila);
        var (resources, bookings, blocks) = await persistence.ReadScheduleAsync(start, end, ct, (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime);
        var bookingBays = bookings.SelectMany(booking => booking.ResourceIds.Select(id =>
            (Id: id, Name: booking.ResourceNames.GetValueOrDefault(id, id))));
        var bays = resources.Select(resource => (resource.Id, resource.Name, resource.Active, resource.Service))
            .Concat(bookingBays.Select(bay => (bay.Id, bay.Name, Active: false, Service: "Regular")))
            .DistinctBy(bay => bay.Id);

        return bays.Select(bay =>
        {
            var entries = bookings.Where(booking => booking.ResourceIds.Contains(bay.Id))
                .Select(ScheduleEntry)
                .Concat(blocks.Where(block => block.ResourceIds.Contains(bay.Id) || (bay.Active && block.ResourceIds.Length == 0))
                    .Select(block => new StaffScheduleEntry(Local(block.StartUtc), Local(block.EndUtc), "Blocked", Reason: block.Reason, BlockId: block.Id)))
                .OrderBy(entry => entry.Start).ThenBy(entry => entry.End).ToArray();
            return new StaffScheduleBay(bay.Name, bay.Active, entries, bay.Service, bay.Id);
        }).Where(bay => bay.Active || bay.Entries.Count > 0).ToArray();
    }

    private static DateTime Local(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(utc, ReservationRules.Manila);

    private StaffScheduleEntry ScheduleEntry(HoleInHub.Data.Reservations.ReservationRecord booking) =>
        new(Local(booking.StartUtc), Local(booking.EndUtc), booking.Status, booking.Id, booking.Reference,
            booking.Service, $"{booking.FirstName} {booking.LastName}".Trim(), booking.Attendees,
            CheckedOut: booking.CheckedOutUtc.HasValue ? Local(booking.CheckedOutUtc.Value) : null,
            Revision: booking.Revision, CanMove: CanMoveStaffBooking(Map(booking), (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime),
            Location: string.Join(", ", booking.ResourceIds.Select(id => booking.ResourceNames.GetValueOrDefault(id, id))));
}
