using MongoDB.Driver;

namespace HoleInHub.Data.Reservations;

public enum AttendanceWriteResult { Applied, AlreadyApplied, Stale, NotFound, NotEligible }
public sealed record AttendanceChange(string Status, string EventType, DateTime AtUtc,
    DateTime? CheckedInUtc, DateTime? CheckedOutUtc, bool CheckAllocation,
    bool ReplaceActualTimes = false, DateTime? EndUtc = null);

public sealed partial class ReservationPersistence
{
    public Task<bool> AttendanceOperationsInstalledAsync(CancellationToken ct) => context.Database
        .GetCollection<MongoDB.Bson.BsonDocument>("allocation_guards")
        .Find(new MongoDB.Bson.BsonDocument("_id", "attendance-operations")).AnyAsync(ct);
    private static FilterDefinition<ReservationRecord> AtRevision(long revision)
    {
        var filter = Builders<ReservationRecord>.Filter;
        return revision == 0 ? filter.Eq(x => x.Revision, 0) | filter.Exists(x => x.Revision, false)
            : filter.Eq(x => x.Revision, revision);
    }

    public async Task<AttendanceWriteResult> RecordAttendanceAsync(string id, long expectedRevision, string commandId,
        string commandHash, string actor, string reason, Func<ReservationRecord, AttendanceChange?> decide, CancellationToken ct,
        Action<ReservationRecord, HoleInHub.Data.Services.ServiceCatalogRecord?>? validateSession = null)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        ct = timeout.Token;
        using var session = await context.Client.StartSessionAsync(cancellationToken: ct);
        return await session.WithTransactionAsync(async (s, token) =>
        {
            var record = await Reservations.Find(s, x => x.Id == id).FirstOrDefaultAsync(token);
            if (record is null) { return AttendanceWriteResult.NotFound; }
            await GuardAsync(s, record, token);
            var prior = record.History.FirstOrDefault(x => x.CommandId == commandId);
            if (prior is not null)
            {
                if (prior.CommandHash != commandHash) { throw new AllocationConflictException("This attendance form was already used with different details. Reload the booking."); }
                return AttendanceWriteResult.AlreadyApplied;
            }
            if (record.Revision != expectedRevision) { return AttendanceWriteResult.Stale; }
            var change = decide(record);
            if (change is null) { return AttendanceWriteResult.NotEligible; }
            var before = ReservationAttendanceSnapshot.From(record);
            var auditDetails = change.EventType is "ReservationNoShow" or "AttendanceCorrected" or "ReservationExtended";
            if (change.EndUtc.HasValue)
            {
                record.EndUtc = change.EndUtc.Value;
                var service = await context.Database.GetCollection<HoleInHub.Data.Services.ServiceCatalogRecord>("service_catalog")
                    .Find(s, x => x.Id == (record.ServiceEntryId ?? record.Service)).FirstOrDefaultAsync(token);
                validateSession?.Invoke(record, service);
            }
            if (change.CheckAllocation && await HasReviewConflictAsync(record, token, s, change.AtUtc))
            {
                return AttendanceWriteResult.NotEligible;
            }
            var update = Builders<ReservationRecord>.Update.Set(x => x.Status, change.Status).Inc(x => x.Revision, 1)
                .Push(x => x.History, new HistoryRecord
                {
                    EventType = change.EventType,
                    ReservationStatus = change.Status,
                    PreviousReservationStatus = record.Status,
                    Status = change.EventType switch
                    {
                        "AttendanceCorrected" => "Attendance corrected",
                        "ReservationExtended" => "Session extended",
                        _ => change.Status
                    },
                    Actor = actor,
                    AtUtc = change.AtUtc,
                    Reason = reason,
                    CommandId = commandId,
                    CommandHash = commandHash,
                    AttendanceBefore = auditDetails ? before : null,
                    AttendanceAfter = auditDetails ? new ReservationAttendanceSnapshot
                    {
                        Status = change.Status,
                        StartUtc = record.StartUtc,
                        EndUtc = record.EndUtc,
                        CheckedInUtc = change.ReplaceActualTimes ? change.CheckedInUtc : change.CheckedInUtc ?? record.CheckedInUtc,
                        CheckedOutUtc = change.ReplaceActualTimes ? change.CheckedOutUtc : change.CheckedOutUtc ?? record.CheckedOutUtc
                    } : null
                });
            if (change.EndUtc.HasValue) { update = update.Set(x => x.EndUtc, change.EndUtc.Value); }
            if (change.ReplaceActualTimes && change.CheckedInUtc is null) { update = update.Unset(x => x.CheckedInUtc); }
            if (change.ReplaceActualTimes && change.CheckedOutUtc is null) { update = update.Unset(x => x.CheckedOutUtc); }
            if (change.CheckedInUtc.HasValue) { update = update.Set(x => x.CheckedInUtc, change.CheckedInUtc); }
            if (change.CheckedOutUtc.HasValue) { update = update.Set(x => x.CheckedOutUtc, change.CheckedOutUtc); }
            var result = await Reservations.UpdateOneAsync(s,
                Builders<ReservationRecord>.Filter.Eq(x => x.Id, id) & AtRevision(expectedRevision), update, cancellationToken: token);
            if (result.ModifiedCount == 1 && change.EventType is "ReservationNoShow" or "ReservationExtended")
            {
                record.Status = change.Status;
                await NotifyAsync(s, record, "attendance:" + commandId,
                    change.EventType == "ReservationNoShow" ? "Reservation marked no-show" : "Reservation extended", token,
                    $"Reservation {record.Reference}: {record.Status}. Session {record.StartUtc:O} to {record.EndUtc:O} (UTC). Use your booking status page for the latest details.");
            }
            return result.ModifiedCount == 1 ? AttendanceWriteResult.Applied : AttendanceWriteResult.Stale;
        }, new TransactionOptions(readConcern: ReadConcern.Snapshot, writeConcern: WriteConcern.WMajority), ct);
    }
}
