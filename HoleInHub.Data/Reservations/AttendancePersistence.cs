using MongoDB.Driver;

namespace HoleInHub.Data.Reservations;

public enum AttendanceWriteResult { Applied, AlreadyApplied, Stale, NotFound, NotEligible }
public sealed record AttendanceChange(string Status, string EventType, DateTime AtUtc,
    DateTime? CheckedInUtc, DateTime? CheckedOutUtc, bool CheckAllocation);

public sealed partial class ReservationPersistence
{
    private static FilterDefinition<ReservationRecord> AtRevision(long revision)
    {
        var filter = Builders<ReservationRecord>.Filter;
        return revision == 0 ? filter.Eq(x => x.Revision, 0) | filter.Exists(x => x.Revision, false)
            : filter.Eq(x => x.Revision, revision);
    }

    public async Task<AttendanceWriteResult> RecordAttendanceAsync(string id, long expectedRevision, string commandId,
        string commandHash, string actor, string reason, Func<ReservationRecord, AttendanceChange?> decide, CancellationToken ct)
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
            if (change is null || change.CheckAllocation && await HasReviewConflictAsync(record, token, s, change.AtUtc))
            {
                return AttendanceWriteResult.NotEligible;
            }
            var update = Builders<ReservationRecord>.Update.Set(x => x.Status, change.Status).Inc(x => x.Revision, 1)
                .Push(x => x.History, new HistoryRecord
                {
                    EventType = change.EventType,
                    ReservationStatus = change.Status,
                    PreviousReservationStatus = record.Status,
                    Status = change.Status,
                    Actor = actor,
                    AtUtc = change.AtUtc,
                    Reason = reason,
                    CommandId = commandId,
                    CommandHash = commandHash
                });
            if (change.CheckedInUtc.HasValue) { update = update.Set(x => x.CheckedInUtc, change.CheckedInUtc); }
            if (change.CheckedOutUtc.HasValue) { update = update.Set(x => x.CheckedOutUtc, change.CheckedOutUtc); }
            var result = await Reservations.UpdateOneAsync(s,
                Builders<ReservationRecord>.Filter.Eq(x => x.Id, id) & AtRevision(expectedRevision), update, cancellationToken: token);
            return result.ModifiedCount == 1 ? AttendanceWriteResult.Applied : AttendanceWriteResult.Stale;
        }, new TransactionOptions(readConcern: ReadConcern.Snapshot, writeConcern: WriteConcern.WMajority), ct);
    }
}
