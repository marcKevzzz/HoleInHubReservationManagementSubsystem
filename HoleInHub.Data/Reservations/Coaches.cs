using System.Globalization;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace HoleInHub.Data.Reservations;

public sealed record CoachWeeklyHours(DayOfWeek Day, int StartMinute, int EndMinute);
public sealed record CoachDateHours(string Date, int? StartMinute, int? EndMinute);
public sealed record CoachAssignmentInterval(string CoachId, DateTime StartUtc, DateTime EndUtc);

public sealed record CoachBookingConflict(string Id, string Reference, DateTime StartUtc, DateTime EndUtc, string Status);

public sealed class CoachScheduleConflictException(CoachBookingConflict[] bookings) : Exception("Reassign the conflicting lessons before changing coach availability.")
{
    public CoachBookingConflict[] Bookings { get; } = bookings;
    public string[] ReservationIds => Bookings.Select(x => x.Id).ToArray();
}

public sealed class CoachRecord
{
    [BsonId] public string Id { get; set; } = "";
    [BsonElement("coach_id")] public string CoachId => Id;
    public string Name { get; set; } = "";
    [BsonIgnoreIfNull] public string? PhotoPath { get; set; }
    [BsonIgnoreIfNull] public string? Title { get; set; }
    [BsonIgnoreIfNull] public string? Phone { get; set; }
    [BsonIgnoreIfNull] public string? Email { get; set; }
    public bool Active { get; set; }
    public long Revision { get; set; }
    public CoachWeeklyHours[] WeeklyHours { get; set; } = [];
    public CoachDateHours[] Overrides { get; set; } = [];

    public IEnumerable<(int Start, int End)> WindowsOn(DateOnly date)
    {
        var day = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var overrides = Overrides.Where(x => x.Date == day).ToArray();
        var windows = overrides.Length > 0
            ? overrides.Where(x => x.StartMinute.HasValue && x.EndMinute.HasValue)
                .Select(x => (Start: x.StartMinute!.Value, End: x.EndMinute!.Value))
            : WeeklyHours.Where(x => x.Day == date.DayOfWeek).Select(x => (Start: x.StartMinute, End: x.EndMinute));
        return windows.OrderBy(x => x.Start);
    }

    public bool Covers(DateTime startUtc, DateTime endUtc)
    {
        if (!Active || endUtc <= startUtc) { return false; }
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Manila");
        var start = TimeZoneInfo.ConvertTimeFromUtc(startUtc, zone);
        var end = TimeZoneInfo.ConvertTimeFromUtc(endUtc, zone);
        var coveredThrough = start;
        foreach (var window in WindowsOn(DateOnly.FromDateTime(start)))
        {
            if (start.Date.AddMinutes(window.Start) > coveredThrough) { break; }
            var until = start.Date.AddMinutes(window.End);
            if (until > coveredThrough) { coveredThrough = until; }
            if (coveredThrough >= end) { return true; }
        }
        return false;
    }
}

public sealed partial class ReservationPersistence
{
    private IMongoCollection<CoachRecord> Coaches => context.Database.GetCollection<CoachRecord>("coaches");

    public Task<List<CoachRecord>> ReadCoachesAsync(CancellationToken ct) =>
        Coaches.Find(_ => true).SortBy(x => x.Name).ThenBy(x => x.Id).ToListAsync(ct);

    public async Task<List<CoachAssignmentInterval>> ReadCoachAssignmentsAsync(DateTime now, DateTime untilUtc, CancellationToken ct)
    {
        var bookings = await Reservations.Find(HoldingCapacity(now) & Builders<ReservationRecord>.Filter.Where(x =>
            x.CoachId != null && x.EndUtc > now && x.StartUtc < untilUtc))
            .Project(x => new CoachAssignmentInterval(x.CoachId!, x.StartUtc, x.EndUtc)).ToListAsync(ct);
        var classes = await Classes.Find(x => x.CoachId != null && x.State != "Released" && x.EndUtc > now && x.StartUtc < untilUtc)
            .Project(x => new CoachAssignmentInterval(x.CoachId!, x.StartUtc, x.EndUtc)).ToListAsync(ct);
        bookings.AddRange(classes);
        return bookings;
    }

    public Task<List<CoachRecord>> AvailableCoachesAsync(DateTime startUtc, DateTime endUtc, DateTime now,
        CancellationToken ct, string? exceptReservationId = null, string? serviceEntryId = null, string? exceptClassId = null) =>
        AvailableCoachesAsync(null, startUtc, endUtc, now, exceptReservationId, ct, assigningExisting: exceptReservationId != null, serviceEntryId: serviceEntryId, exceptClassId: exceptClassId);

    private async Task<List<CoachRecord>> AvailableCoachesAsync(IClientSessionHandle? session, DateTime start,
        DateTime end, DateTime now, string? exceptId, CancellationToken ct, bool assigningExisting = false, string? serviceEntryId = null, string? exceptClassId = null)
    {
        var conflicts = HoldingCapacity(now) & Builders<ReservationRecord>.Filter.Where(x => (x.Service == "PhilippineCoach" || x.CoachId != null)
            && x.Id != exceptId && x.StartUtc < end && x.EndUtc > start);
        var bookings = session == null ? Reservations.Find(conflicts) : Reservations.Find(session, conflicts);
        var occupied = (await bookings.Project(x => x.CoachId).ToListAsync(ct)).ToHashSet();
        if (!assigningExisting && occupied.Contains(null)) { return []; }
        var classFilter = Builders<ClassSessionRecord>.Filter.Where(x => x.ClassSessionId != exceptClassId && x.State != "Released" && x.StartUtc < end && x.EndUtc > start && x.CoachId != null);
        var classes = session == null ? Classes.Find(classFilter) : Classes.Find(session, classFilter);
        occupied.UnionWith(await classes.Project(x => x.CoachId).ToListAsync(ct));
        string[]? eligible = null;
        if (serviceEntryId is not null)
        {
            var catalog = context.Database.GetCollection<HoleInHub.Data.Services.ServiceCatalogRecord>("service_catalog");
            var offering = session == null ? await catalog.Find(x => x.Id == serviceEntryId).FirstOrDefaultAsync(ct)
                : await catalog.Find(session, x => x.Id == serviceEntryId).FirstOrDefaultAsync(ct);
            eligible = offering?.CoachIds;
        }
        var directory = session == null ? Coaches.Find(x => x.Active) : Coaches.Find(session, x => x.Active);
        var coaches = await directory.SortBy(x => x.Id).ToListAsync(ct);
        return coaches.Where(x => (eligible == null || eligible.Contains(x.Id)) && !occupied.Contains(x.Id) && x.Covers(start, end)).ToList();
    }

    public async Task<bool> SaveCoachAsync(CoachRecord coach, long? expectedRevision, DateTime now, CancellationToken ct, IReadOnlyList<HoleInHub.Data.Services.ServiceImageRecord>? images = null)
    {
        using var session = await context.Client.StartSessionAsync(cancellationToken: ct);
        return await session.WithTransactionAsync(async (s, token) =>
        {
            await GuardCatalogAsync(s, token);
            var current = await Coaches.Find(s, x => x.Id == coach.Id).FirstOrDefaultAsync(token);
            if (current?.Revision != expectedRevision) { return false; }
            var assignments = await Reservations.Find(s, HoldingCapacity(now) & Builders<ReservationRecord>.Filter
                .Where(x => x.CoachId == coach.Id && x.EndUtc > now)).ToListAsync(token);
            var classCommitments = await Classes.Find(s, x => x.CoachId == coach.Id && x.State != "Released" && x.EndUtc > now).ToListAsync(token);
            if (classCommitments.Any(x => !coach.Covers(x.StartUtc, x.EndUtc)))
            { throw new CoachScheduleConflictException(classCommitments.Where(x => !coach.Covers(x.StartUtc, x.EndUtc)).Select(x => new CoachBookingConflict(x.ClassSessionId, "Class " + x.ClassSessionId, x.StartUtc, x.EndUtc, x.State)).ToArray()); }
            var conflicting = assignments.Where(x => !coach.Covers(x.StartUtc, x.EndUtc))
                .OrderBy(x => x.StartUtc).ThenBy(x => x.Reference, StringComparer.Ordinal)
                .Select(x => new CoachBookingConflict(x.Id, x.Reference, x.StartUtc, x.EndUtc, x.Status)).ToArray();
            if (conflicting.Length > 0)
            {
                throw new CoachScheduleConflictException(conflicting);
            }
            await HoleInHub.Data.Services.ServiceCatalogPersistence.StoreImagesAsync(context, s, images ?? [], token);
            coach.Revision = (expectedRevision ?? 0) + 1;
            await Coaches.ReplaceOneAsync(s, x => x.Id == coach.Id, coach, new ReplaceOptions { IsUpsert = true }, token);
            return true;
        }, new TransactionOptions(readConcern: ReadConcern.Snapshot, writeConcern: WriteConcern.WMajority), ct);
    }

    private async Task ClaimCoachAsync(IClientSessionHandle session, ReservationRecord booking, string? requestedId,
        DateTime now, CancellationToken ct, bool assigningExisting = false)
    {
        var available = await AvailableCoachesAsync(session, booking.StartUtc, booking.EndUtc, now, booking.Id, ct, assigningExisting, booking.ServiceEntryId ?? booking.Service);
        var selected = available.FirstOrDefault(x => requestedId == null || x.Id == requestedId)
            ?? throw new AllocationConflictException("No available coach matches this lesson time.", sessionUnavailable: true);
        var day = TimeZoneInfo.ConvertTimeFromUtc(booking.StartUtc, TimeZoneInfo.FindSystemTimeZoneById("Asia/Manila"))
            .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        await context.Database.GetCollection<BsonDocument>("allocation_guards").UpdateOneAsync(session,
            new BsonDocument("_id", $"coach:{selected.Id}:{day}"), new BsonDocument("$inc", new BsonDocument("version", 1)),
            new UpdateOptions { IsUpsert = true }, ct);
        booking.CoachId = selected.Id;
        booking.CoachName = selected.Name;
    }

    public async Task<bool> AssignClassCoachAsync(string classId, long revision, string? coachId, DateTime now, CancellationToken ct)
    {
        using var session = await context.Client.StartSessionAsync(cancellationToken: ct);
        return await session.WithTransactionAsync(async (s, token) =>
        {
            await GuardCatalogAsync(s, token);
            var lesson = await Classes.Find(s, x => x.ClassSessionId == classId).FirstOrDefaultAsync(token);
            if (lesson is null || lesson.State == "Released" || lesson.StartUtc <= now || lesson.ScheduleRevision != revision) { return false; }
            // Remove only inside this transaction so the class does not conflict with itself.
            await Classes.DeleteOneAsync(s, x => x.Id == lesson.Id, cancellationToken: token);
            var allocation = new ReservationRecord
            {
                Service = "KpgaGroup",
                ServiceEntryId = lesson.ServiceEntryId,
                StartUtc = lesson.StartUtc,
                EndUtc = lesson.EndUtc,
                ResourceIds = lesson.ResourceIds
            };
            await ClaimCoachAsync(s, allocation, coachId, now, token, assigningExisting: true);
            lesson.CoachId = allocation.CoachId;
            lesson.CoachName = allocation.CoachName;
            lesson.ScheduleRevision++;
            await Classes.InsertOneAsync(s, lesson, cancellationToken: token);
            return true;
        }, new TransactionOptions(readConcern: ReadConcern.Snapshot, writeConcern: WriteConcern.WMajority), ct);
    }

    public async Task<bool> AssignCoachAsync(string reservationId, long revision, string coachId, string actor, DateTime now, CancellationToken ct)
    {
        using var session = await context.Client.StartSessionAsync(cancellationToken: ct);
        return await session.WithTransactionAsync(async (s, token) =>
        {
            await GuardCatalogAsync(s, token);
            var booking = await Reservations.Find(s, x => x.Id == reservationId).FirstOrDefaultAsync(token);
            if (booking == null || booking.Revision != revision || booking.Service is not ("PhilippineCoach" or "KpgaPro") ||
                booking.StartUtc <= now || booking.Status is not ("Pending" or "Confirmed") ||
                booking.Status == "Pending" && booking.ReviewDeadlineUtc <= now) { return false; }
            var oldName = booking.CoachName;
            await ClaimCoachAsync(s, booking, coachId, now, token, assigningExisting: true);
            var update = Builders<ReservationRecord>.Update.Set(x => x.CoachId, booking.CoachId)
                .Set(x => x.CoachName, booking.CoachName).Inc(x => x.Revision, 1)
                .Push(x => x.History, new HistoryRecord
                {
                    EventType = "CoachAssigned",
                    Status = "Coach assigned",
                    ReservationStatus = booking.Status,
                    Actor = actor,
                    AtUtc = now,
                    Reason = oldName == null ? booking.CoachName! : $"{oldName} → {booking.CoachName}"
                });
            if (booking.Status == "Pending") { update = update.Set(x => x.CoachConfirmed, false); }
            await Reservations.UpdateOneAsync(s, x => x.Id == reservationId, update, cancellationToken: token);
            return true;
        }, new TransactionOptions(readConcern: ReadConcern.Snapshot, writeConcern: WriteConcern.WMajority), ct);
    }
}
