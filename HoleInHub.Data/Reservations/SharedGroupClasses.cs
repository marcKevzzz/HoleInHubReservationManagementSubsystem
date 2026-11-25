using MongoDB.Driver;

namespace HoleInHub.Data.Reservations;

public sealed partial class ReservationPersistence
{
    private IMongoCollection<ClassSessionRecord> Classes => context.Database.GetCollection<ClassSessionRecord>("class_sessions");
    private static FilterDefinition<ClassSessionRecord> ClassAllocations(DateTime start, DateTime end) =>
        Builders<ClassSessionRecord>.Filter.Where(x => x.State != "Released" && x.StartUtc < end && x.EndUtc > start);

    public Task<List<ClassSessionRecord>> ClassesAsync(DateTime startUtc, DateTime endUtc, CancellationToken ct) =>
        Classes.Find(ClassAllocations(startUtc, endUtc)).SortBy(x => x.StartUtc).ThenBy(x => x.Id).ToListAsync(ct);

    public Task<ClassSessionRecord?> ClassAsync(string classSessionId, CancellationToken ct) =>
        Classes.Find(x => x.ClassSessionId == classSessionId).FirstOrDefaultAsync(ct)!;

    public Task<List<ReservationRecord>> ClassRosterAsync(string classSessionId, CancellationToken ct) =>
        Reservations.Find(x => x.ClassSessionId == classSessionId || (x.PackageDates != null && x.PackageDates.Any(d => d.ClassSessionId == classSessionId))).SortBy(x => x.CreatedUtc).ThenBy(x => x.Id).ToListAsync(ct);

    public async Task<bool> SetClassReminderAsync(string id, string? publicBase, CancellationToken ct) =>
        (await Reservations.UpdateOneAsync(Builders<ReservationRecord>.Filter.Ne(x => x.NextClassReminderQueued, true) & Builders<ReservationRecord>.Filter.Where(x => x.Id == id && x.ClassSessionId != null
            && (x.Status == "Pending" || x.Status == "Confirmed" || x.Status == "Completed" || x.Status == "Checked-In")),
            publicBase == null ? Builders<ReservationRecord>.Update.Unset(x => x.NextClassReminderBase)
                : Builders<ReservationRecord>.Update.Set(x => x.NextClassReminderBase, publicBase), cancellationToken: ct)).MatchedCount == 1;

    private async Task QueueClassRemindersAsync(IClientSessionHandle session, ClassSessionRecord occurrence, DateTime now, CancellationToken ct)
    {
        var families = await Reservations.Find(session, x => x.NextClassReminderBase != null && x.ClassSessionId != null && x.PackageId == null
            && (x.ServiceEntryId ?? x.Service) == occurrence.ServiceEntryId && x.StartUtc < occurrence.StartUtc
            && (x.Status == "Pending" || x.Status == "Confirmed" || x.Status == "Completed" || x.Status == "Checked-In")).ToListAsync(ct);
        foreach (var family in families)
        {
            var seats = family.Learners?.Count(x => x.State == "Enrolled") ?? 0;
            if (seats == 0 || string.IsNullOrWhiteSpace(family.Email)) { continue; }
            var service = Uri.EscapeDataString("KpgaGroup:" + occurrence.ServiceEntryId);
            var url = $"{family.NextClassReminderBase}/Reservations/Create?service={service}&classSessionId={occurrence.ClassSessionId}";
            var fit = seats <= occurrence.Capacity ? "Your group fits at publication." : "There are too few seats for your whole group.";
            await context.Database.GetCollection<OutboxRecord>("outbox").InsertOneAsync(session, new OutboxRecord
            {
                Id = $"{family.Id}:next-class",
                Recipient = family.Email,
                ReservationId = family.Id,
                DueUtc = now,
                ExpiresUtc = occurrence.StartUtc,
                Subject = "A new KPGA group class is open",
                Body = $"KPGA group class: {occurrence.StartUtc.AddHours(8):ddd, dd MMM yyyy, h:mm tt} Manila time. " +
                    $"₱{occurrence.PricePerLearner:N2} per learner. {occurrence.Capacity} seats at publication. Your group: {seats}. {fit} " +
                    $"Check current availability and book: {url}"
            }, cancellationToken: ct);
            await Reservations.UpdateOneAsync(session, x => x.Id == family.Id, Builders<ReservationRecord>.Update
                .Unset(x => x.NextClassReminderBase).Set(x => x.NextClassReminderQueued, true), cancellationToken: ct);
        }
    }

    public async Task<ClassSessionRecord> PublishClassAsync(ClassSessionRecord occurrence, DateTime now, CancellationToken ct)
    {
        using var session = await context.Client.StartSessionAsync(cancellationToken: ct);
        return await session.WithTransactionAsync(async (s, token) =>
        {
            var allocation = new ReservationRecord { StartUtc = occurrence.StartUtc, EndUtc = occurrence.EndUtc, ResourceIds = occurrence.ResourceIds };
            await GuardAsync(s, allocation, token);
            var prior = await Classes.Find(s, x => x.Id == occurrence.Id).FirstOrDefaultAsync(token);
            if (prior is not null)
            {
                if (prior.ServiceEntryId != occurrence.ServiceEntryId || prior.StartUtc != occurrence.StartUtc || prior.EndUtc != occurrence.EndUtc ||
                    prior.Capacity != occurrence.Capacity || prior.PricePerLearner != occurrence.PricePerLearner || prior.CreatedBy != occurrence.CreatedBy ||
                    !prior.ResourceIds.Order().SequenceEqual(occurrence.ResourceIds.Order()))
                { throw new AllocationConflictException("This class submission has different details. Reload the schedule."); }
                return prior;
            }
            var offering = await context.Database.GetCollection<HoleInHub.Data.Services.ServiceCatalogRecord>("service_catalog")
                .Find(s, x => x.Id == occurrence.ServiceEntryId && x.BookingService == "KpgaGroup" && x.Active && !x.Deleted).FirstOrDefaultAsync(token);
            var bays = await Resources.Find(s, Builders<ResourceRecord>.Filter.In(x => x.Id, occurrence.ResourceIds) & Builders<ResourceRecord>.Filter.Where(x => x.Active && x.Service == "Regular")).ToListAsync(token);
            if (offering is null || occurrence.StartUtc <= now || occurrence.EndUtc <= occurrence.StartUtc || occurrence.State != "Open" ||
                occurrence.HeldSeats != 0 || occurrence.PricePerLearner <= 0 || occurrence.Capacity is < 1 or > 8 ||
                occurrence.Capacity > (offering.MaxPax ?? 8) || bays.Count != occurrence.ResourceIds.Length ||
                occurrence.Capacity > bays.Sum(x => Math.Min(x.MaxPlayers ?? 6, x.MaxTotalGuests ?? 8)))
            { throw new AllocationConflictException("Check the class offering, time, price, active bays and learner capacity."); }
            if (await HasAllocationConflictAsync(allocation, s, token, now))
            { throw new AllocationConflictException("A booking, block or class already holds these bays."); }
            await Classes.InsertOneAsync(s, occurrence, cancellationToken: token);
            await QueueClassRemindersAsync(s, occurrence, now, token);
            return occurrence;
        }, cancellationToken: ct);
    }

    public async Task<bool> CloseClassAsync(string classSessionId, CancellationToken ct) =>
        (await Classes.UpdateOneAsync(x => x.ClassSessionId == classSessionId && x.State != "Released",
            Builders<ClassSessionRecord>.Update.Set(x => x.State, "Closed"), cancellationToken: ct)).MatchedCount == 1;

    public async Task<bool> ReleaseClassAsync(string classSessionId, CancellationToken ct)
    {
        using var session = await context.Client.StartSessionAsync(cancellationToken: ct);
        return await session.WithTransactionAsync(async (s, token) =>
        {
            var item = await Classes.Find(s, x => x.ClassSessionId == classSessionId).FirstOrDefaultAsync(token);
            if (item is null) { return false; }
            if (item.State == "Released") { return true; }
            if (item.State != "Closed" || item.HeldSeats > 0) { return false; }
            await GuardAsync(s, new ReservationRecord { StartUtc = item.StartUtc, ResourceIds = item.ResourceIds }, token);
            return (await Classes.UpdateOneAsync(s, x => x.Id == item.Id && x.State == "Closed" && x.HeldSeats == 0,
                Builders<ClassSessionRecord>.Update.Set(x => x.State, "Released"), cancellationToken: token)).MatchedCount == 1;
        }, cancellationToken: ct);
    }

    private async Task ClaimSeatsAsync(IClientSessionHandle session, ReservationRecord record, DateTime now, CancellationToken ct)
    {
        var learners = record.Learners;
        if (record.Service != "KpgaGroup" || record.Status != "Pending" || learners is not { Length: >= 1 and <= 8 } ||
            learners.Any(x => x.State != "Enrolled" || x.Attendance != "Unmarked") ||
            learners.Select(x => x.Id).Distinct().Count() != learners.Length || learners.Length != record.Attendees)
        { throw new AllocationConflictException("Check the class learner roster."); }
        if (record.PackageId != null)
        {
            var package = await Packages.FindOneAndUpdateAsync(session,
                x => x.PackageId == record.PackageId && x.Open && x.PricePerLearner == record.PricePerLearner,
                Builders<ClassPackageRecord>.Update.Inc(x => x.Revision, 1), cancellationToken: ct);
            if (package == null || package.ServiceEntryId != (record.ServiceEntryId ?? record.Service)
                || package.ClassSessionIds[0] != record.ClassSessionId || now >= (package.RegistrationEndsUtc ?? record.StartUtc.AddHours(8).Date.AddHours(-8)))
            { throw new AllocationConflictException("The package changed. Review its dates and price again.", true); }
            var dates = new List<ClassSessionRecord>();
            foreach (var id in package.ClassSessionIds)
            {
                var date = await Classes.FindOneAndUpdateAsync(session, x => x.ClassSessionId == id && x.State == "Open" &&
                    x.StartUtc > now && x.HeldSeats <= x.Capacity - learners.Length,
                    Builders<ClassSessionRecord>.Update.Inc(x => x.HeldSeats, learners.Length), cancellationToken: ct);
                if (date == null)
                {
                    var unavailable = await Classes.Find(session, x => x.ClassSessionId == id).FirstOrDefaultAsync(ct);
                    throw new AllocationConflictException($"The lesson on {unavailable?.StartUtc.AddHours(8):ddd, d MMM} cannot accommodate your family. Choose another package.", true);
                }
                dates.Add(date);
            }
            record.PackageSeats = dates.SelectMany(d => learners.Select(l => new PackageSeat { SessionId = d.ClassSessionId, LearnerId = l.Id })).ToArray();
            record.PackageDates = dates.ToArray();
            return;
        }
        var item = await Classes.Find(session, x => x.ClassSessionId == record.ClassSessionId).FirstOrDefaultAsync(ct);
        if (item is null || item.State != "Open" || item.StartUtc <= now || item.StartUtc != record.StartUtc || item.EndUtc != record.EndUtc ||
            item.ServiceEntryId != (record.ServiceEntryId ?? record.Service) || item.PricePerLearner != record.PricePerLearner ||
            !item.ResourceIds.Order().SequenceEqual(record.ResourceIds.Order()))
        { throw new AllocationConflictException("The class changed or enrollment closed. Review the available classes.", true); }
        var result = await Classes.UpdateOneAsync(session, x => x.Id == item.Id && x.State == "Open" && x.HeldSeats <= item.Capacity - learners.Length,
            Builders<ClassSessionRecord>.Update.Inc(x => x.HeldSeats, learners.Length), cancellationToken: ct);
        if (result.ModifiedCount != 1) { throw new AllocationConflictException("The class has too few seats left for this family.", true); }
    }

    private async Task ReturnSeatsAsync(IClientSessionHandle session, string classId, int count, CancellationToken ct)
    {
        if (count == 0) { return; }
        var changed = await Classes.UpdateOneAsync(session, x => x.ClassSessionId == classId && x.HeldSeats >= count,
            Builders<ClassSessionRecord>.Update.Inc(x => x.HeldSeats, -count), cancellationToken: ct);
        if (changed.ModifiedCount != 1) { throw new AllocationConflictException("The class seat count requires staff review."); }
    }

    private async Task CancelFamilySeatsAsync(IClientSessionHandle session, ReservationRecord record, CancellationToken ct)
    {
        var enrolled = record.Learners!.Where(x => x.State == "Enrolled").ToArray();
        foreach (var id in record.PackageDates?.Select(x => x.ClassSessionId) ?? [record.ClassSessionId!])
        { await ReturnSeatsAsync(session, id, record.PackageSeats?.Count(x => x.SessionId == id && x.State != "Cancelled") ?? enrolled.Length, ct); }
        foreach (var learner in enrolled) { learner.State = "Cancelled"; }
        foreach (var seat in record.PackageSeats ?? []) { seat.State = "Cancelled"; }
    }

    public Task<bool> CancelLearnerAsync(string reservationId, string learnerId, string actor, DateTime now, CancellationToken ct) =>
        UpdateLearnerAsync(reservationId, learnerId, actor, null, now, ct);

    public Task<bool> SetLearnerAttendanceAsync(string reservationId, string learnerId, string attendance, string actor, DateTime now, CancellationToken ct) =>
        attendance is "Present" or "Absent" ? UpdateLearnerAsync(reservationId, learnerId, actor, attendance, now, ct) : Task.FromResult(false);

    private async Task<bool> UpdateLearnerAsync(string reservationId, string learnerId, string actor, string? attendance, DateTime now, CancellationToken ct)
    {
        using var session = await context.Client.StartSessionAsync(cancellationToken: ct);
        return await session.WithTransactionAsync(async (s, token) =>
        {
            var record = await Reservations.Find(s, x => x.Id == reservationId).FirstOrDefaultAsync(token);
            var learner = record?.Learners?.SingleOrDefault(x => x.Id == learnerId);
            if (record?.ClassSessionId is null || record.PackageId != null || learner is null) { return false; }
            if (attendance is null)
            {
                if (learner.State == "Cancelled") { return true; }
                if (record.Status is not ("Pending" or "Confirmed") || record.StartUtc < now.AddHours(1)) { return false; }
                await ReturnSeatsAsync(s, record.ClassSessionId, 1, token);
                learner.State = "Cancelled";
                if (record.Learners!.All(x => x.State == "Cancelled")) { record.Status = "Cancelled"; }
            }
            else
            {
                if (learner.State != "Enrolled" || record.Status is not ("Confirmed" or "Checked-In" or "Completed") || now < record.StartUtc) { return false; }
                if (learner.Attendance == attendance) { return true; }
                learner.Attendance = attendance;
            }
            var update = Builders<ReservationRecord>.Update.Set(x => x.Learners, record.Learners).Set(x => x.Status, record.Status)
                .Inc(x => x.Revision, 1).Push(x => x.History, new HistoryRecord
                {
                    Status = attendance is null ? "Learner cancelled" : "Learner attendance",
                    ReservationStatus = record.Status,
                    AtUtc = now,
                    Actor = actor,
                    Reason = learnerId + (attendance is null ? "" : " " + attendance)
                });
            await Reservations.UpdateOneAsync(s, x => x.Id == record.Id, update, cancellationToken: token);
            return true;
        }, cancellationToken: ct);
    }
}
