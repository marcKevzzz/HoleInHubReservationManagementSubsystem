using HoleInHub.Data.Reservations;

namespace HoleInHub.Business.Reservations;

public sealed record StaffClassSession(string Id, string Offering, DateTime StartUtc, DateTime EndUtc, string[] Bays, int Capacity, int HeldSeats, string State, decimal Price);
public sealed record StaffClassOffering(string Id, string Name);
public sealed record PackageSeatView(string SessionId, DateTime StartUtc, string LearnerId, string State, string Attendance);
public sealed record PackageSchedule(string Id, string[] Sessions, decimal Price);
public sealed record PackageEditor(string Offering, string Title, int Count, decimal Price, StaffClassSession[] Sessions, PackageSchedule[] Packages);

public sealed partial class ReservationService
{
    public async Task<PackageEditor?> PackageEditorAsync(string offering, CancellationToken ct)
    {
        if (!SharedClassesEnabled || serviceCatalog == null) { return null; }
        var item = (await serviceCatalog.GetActiveAsync(ct)).FirstOrDefault(x => x.Id == offering && x.BookingService == "KpgaGroup");
        if (item == null) { return null; }
        var now = (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime;
        var sessions = (await persistence.ClassesAsync(now, DateTime.MaxValue, ct))
            .Where(x => x.ServiceEntryId == offering && x.State == "Open" && x.StartUtc > now).Select(StaffClass).ToArray();
        var packages = (await persistence.PackagesAsync(offering, ct)).Select(x => new PackageSchedule(x.PackageId, x.ClassSessionIds, x.PricePerLearner)).ToArray();
        var pricing = item.Pricing ?? PricingRules.Default("KpgaGroup");
        return new(offering, item.Title, pricing.ClassCount, pricing.BaseRate, sessions, packages);
    }

    public async Task PublishPackageAsync(string offering, string[] ids, DateOnly deadline, CancellationToken ct)
    {
        if (!SharedClassesEnabled) { throw new ReservationValidationException("Package publication is closed."); }
        try { await persistence.PublishPackageAsync(offering, ids, PricingRules.Default("KpgaGroup"), (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime, ct, DashboardMetrics.Utc(deadline.AddDays(1))); }
        catch (AllocationConflictException ex) { throw new ReservationValidationException(ex.Message); }
    }

    public Task<bool> ClosePackageAsync(string offering, string id, CancellationToken ct) => persistence.ClosePackageAsync(offering, id, ct);

    public async Task<StaffClassOffering[]> GroupOfferingsAsync(CancellationToken ct) =>
        serviceCatalog is null ? [] : (await serviceCatalog.GetActiveAsync(ct)).Where(x => x.BookingService == "KpgaGroup")
            .Select(x => new StaffClassOffering(x.Id, x.Title)).ToArray();

    private static StaffClassSession StaffClass(ClassSessionRecord x) =>
        new(x.ClassSessionId, x.ServiceEntryId, x.StartUtc, x.EndUtc, x.ResourceIds, x.Capacity, x.HeldSeats, x.State, x.PricePerLearner);

    public async Task<StaffClassSession[]> StaffClassesAsync(DateOnly from, CancellationToken ct) =>
        (await persistence.ClassesAsync(DashboardMetrics.Utc(from), DashboardMetrics.Utc(from.AddDays(31)), ct)).Select(StaffClass).ToArray();

    public async Task<string> PublishGroupClassAsync(DateOnly day, string offering, string[] bays, int capacity, decimal price, string actor, CancellationToken ct)
    {
        var now = (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime;
        var start = DashboardMetrics.Utc(day).AddHours(15);
        if (!SharedClassesEnabled || start <= now || day.DayOfWeek != DayOfWeek.Tuesday ||
            !(await GroupOfferingsAsync(ct)).Any(x => x.Id == offering))
        {
            throw new ReservationValidationException("Choose an active group lesson and a future Tuesday.");
        }

        var resources = (await GetCatalogAsync(ct)).Where(x => bays.Contains(x.Id) && x.Service == "Regular").ToArray();
        if (resources.Length != bays.Length || capacity is < 1 or > 8 || capacity > resources.Sum(x => Math.Min(x.MaxPlayers, x.MaxTotalGuests)))
        {
            throw new ReservationValidationException("Choose regular bays with enough player capacity for every learner.");
        }

        if (price <= 0 || decimal.Round(price, 2) != price)
        {
            throw new ReservationValidationException("Enter the approved single-class price per learner in pesos.");
        }

        try
        {
            var saved = await persistence.PublishClassAsync(new ClassSessionRecord
            {
                ServiceEntryId = offering,
                StartUtc = start,
                EndUtc = start.AddHours(1),
                ResourceIds = bays,
                Capacity = capacity,
                PricePerLearner = price,
                CreatedBy = actor
            }, now, ct);
            return saved.ClassSessionId;
        }
        catch (AllocationConflictException error) { throw new ReservationValidationException(error.Message); }
    }

    public async Task<StaffClassSession?> StaffClassSessionAsync(string id, CancellationToken ct) =>
        await persistence.ClassAsync(id, ct) is { } session ? StaffClass(session) : null;

    public async Task<(StaffClassSession Session, ReservationDto[] Bookings)?> StaffClassRosterAsync(string id, CancellationToken ct)
    {
        var session = await persistence.ClassAsync(id, ct);
        return session is null ? null : (StaffClass(session), (await persistence.ClassRosterAsync(id, ct)).Select(x => Map(x)).ToArray());
    }

    public async Task<bool> MarkGroupAttendanceAsync(string classId, string reservationId, string learnerId, string attendance, string actor, CancellationToken ct)
    {
        if (attendance is not ("Present" or "Absent"))
        {
            return false;
        }

        var booking = await persistence.ByIdAsync(reservationId, ct);
        var now = (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime;
        if (booking?.PackageId != null) { return await UpdatePackageSeatAsync(reservationId, classId, learnerId, attendance, ct); }
        if (booking?.ClassSessionId != classId || booking.StartUtc > now || booking.Status is not ("Confirmed" or "Checked-In"))
        {
            return false;
        }

        return await persistence.SetLearnerAttendanceAsync(reservationId, learnerId, attendance, actor, now, ct);
    }

    public Task<bool> UpdatePackageSeatAsync(string bookingId, string sessionId, string learnerId, string action, CancellationToken ct) =>
        persistence.UpdatePackageSeatAsync(bookingId, sessionId, learnerId, action, (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime, ct);

    public Task<bool> CloseGroupClassAsync(string id, CancellationToken ct) => persistence.CloseClassAsync(id, ct);
    public Task<bool> ReleaseGroupClassAsync(string id, CancellationToken ct) => persistence.ReleaseClassAsync(id, ct);
}
