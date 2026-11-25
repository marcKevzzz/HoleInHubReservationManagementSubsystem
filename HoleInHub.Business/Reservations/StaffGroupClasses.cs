using HoleInHub.Data.Reservations;

namespace HoleInHub.Business.Reservations;

public sealed record StaffClassSession(string Id, string Offering, DateTime StartUtc, DateTime EndUtc, string[] Bays, int Capacity, int HeldSeats, string State, decimal Price, long Revision = 0);
public sealed record StaffClassOffering(string Id, string Name);
public sealed record PackageSeatView(string SessionId, DateTime StartUtc, string LearnerId, string State, string Attendance);
public sealed record PackageSchedule(string Id, string[] Sessions, decimal Price, DateTime? Deadline = null);
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
        var packages = (await persistence.PackagesAsync(offering, ct)).Select(x => new PackageSchedule(x.PackageId, x.ClassSessionIds, x.PricePerLearner, x.RegistrationEndsUtc)).ToArray();
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
        new(x.ClassSessionId, x.ServiceEntryId, x.StartUtc, x.EndUtc, x.ResourceIds, x.Capacity, x.HeldSeats, x.State, x.PricePerLearner, x.ScheduleRevision);

    public async Task<StaffClassSession[]> StaffClassesAsync(DateOnly from, CancellationToken ct) =>
        (await persistence.ClassesAsync(DashboardMetrics.Utc(from), DashboardMetrics.Utc(from.AddDays(31)), ct)).Select(StaffClass).ToArray();

    public async Task<string> PublishGroupClassAsync(DateOnly day, string offering, string[] bays, int capacity, string actor, CancellationToken ct, TimeOnly? startTime = null, TimeOnly? endTime = null, string? editing = null, long revision = 0, bool weeklyPackage = false)
    {
        var now = (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime;
        var from = startTime ?? new TimeOnly(15, 0);
        var until = endTime ?? new TimeOnly(16, 0);
        var endMinutes = until == TimeOnly.MinValue ? 1440 : until.ToTimeSpan().TotalMinutes;
        var duration = endMinutes - from.ToTimeSpan().TotalMinutes;
        if (duration <= 0 || duration % 30 != 0)
        { throw new ReservationValidationException("Choose an end time after the start, in 30-minute increments."); }
        var (start, end) = ReservationRules.Interval(day, from, (int)duration);
        if (!SharedClassesEnabled || start <= now ||
            !(await GroupOfferingsAsync(ct)).Any(x => x.Id == offering))
        {
            throw new ReservationValidationException("Choose an active group lesson and a future class date.");
        }

        var resources = (await GetCatalogAsync(ct)).Where(x => bays.Contains(x.Id) && x.Service == "Regular").ToArray();
        if (resources.Length != bays.Length || capacity is < 1 or > 10 || capacity > resources.Sum(x => Math.Min(x.MaxPlayers, x.MaxTotalGuests)))
        {
            throw new ReservationValidationException("Choose regular bays with enough player capacity for every learner.");
        }

        try
        {
            var count = 1;
            if (weeklyPackage)
            {
                if (editing != null) { throw new ReservationValidationException("Edit one scheduled class at a time."); }
                var editor = await PackageEditorAsync(offering, ct) ?? throw new ReservationValidationException("Choose an active lesson program.");
                count = editor.Count;
                if (count < 1 || (DateTime.MaxValue - end).TotalDays < 7L * (count - 1))
                { throw new ReservationValidationException("Choose a valid first date for this series."); }
            }
            var occurrences = Enumerable.Range(0, count).Select(week => new ClassSessionRecord
            {
                ServiceEntryId = offering,
                StartUtc = start.AddDays(7 * week),
                EndUtc = end.AddDays(7 * week),
                ResourceIds = bays,
                Capacity = capacity,
                CreatedBy = actor
            }).ToArray();
            if (weeklyPackage)
            {
                await persistence.PublishClassSeriesAsync(occurrences, now, ct);
                return occurrences[0].ClassSessionId;
            }
            var saved = await persistence.PublishClassAsync(occurrences[0], now, ct, editing, revision);
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
