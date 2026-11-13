using MongoDB.Bson;
using MongoDB.Driver;

namespace HoleInHub.Data.Reservations;

public sealed record BatchMoveRevision(string Id, long Revision);

public sealed partial class ReservationPersistence
{
    public async Task MoveStaffBatchAsync(IReadOnlyList<BatchMoveRevision> selected, string commandId, string hash,
        string actor, string day, DateTime now, Func<ReservationRecord, (DateTime Start, DateTime End)> destination,
        CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        using var session = await context.Client.StartSessionAsync(cancellationToken: timeout.Token);
        await session.WithTransactionAsync(async (s, token) =>
        {
            var ids = selected.Select(item => item.Id).ToArray();
            var bookings = await Reservations.Find(s, Builders<ReservationRecord>.Filter.In(x => x.Id, ids))
                .ToListAsync(token);
            if (bookings.Count != selected.Count)
            {
                throw new AllocationConflictException("A selected booking is no longer available.");
            }

            var prior = bookings.Select(booking => booking.History.FirstOrDefault(entry => entry.CommandId == commandId)).ToArray();
            if (prior.Any(entry => entry is not null))
            {
                if (prior.All(entry => entry?.CommandHash == hash)) { return true; }
                throw new AllocationConflictException("This move was submitted with different details. Reload the schedule.");
            }

            await GuardCatalogAsync(s, token);
            var revisions = selected.ToDictionary(item => item.Id, item => item.Revision);
            var guards = context.Database.GetCollection<BsonDocument>("allocation_guards");
            foreach (var key in bookings.SelectMany(booking => booking.ResourceIds)
                         .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
            {
                await guards.UpdateOneAsync(s, new BsonDocument("_id", $"{key}:{day}"),
                    new BsonDocument("$inc", new BsonDocument("version", 1)), new UpdateOptions { IsUpsert = true }, token);
            }

            var catalog = (await Resources.Find(s, x => x.Active).ToListAsync(token)).ToDictionary(bay => bay.Id);
            foreach (var booking in bookings)
            {
                if (booking.Revision != revisions[booking.Id])
                {
                    throw new AllocationConflictException($"Booking {booking.Reference} changed. Reload the schedule.");
                }
                var (start, end) = destination(booking);
                if (booking.ResourceIds.Length == 0 || booking.ResourceIds.Any(id =>
                    !catalog.TryGetValue(id, out var bay) || bay.Service != booking.Service))
                {
                    throw new AllocationConflictException($"An assigned bay for {booking.Reference} is unavailable.");
                }

                var change = new ScheduleChangeRecord
                {
                    Id = commandId, PayloadHash = hash, Status = "Approved", RequestedUtc = now, DecidedUtc = now,
                    StartUtc = start, EndUtc = end, Reason = "Moved with other bookings on the staff timeline.",
                    BaseRevision = booking.Revision, OriginalStartUtc = booking.StartUtc, OriginalEndUtc = booking.EndUtc,
                    OriginalResourceIds = booking.ResourceIds, ResourceIds = booking.ResourceIds, ResourceNames = booking.ResourceNames
                };
                var update = Builders<ReservationRecord>.Update.Set(x => x.ScheduleChange, change)
                    .Set(x => x.StartUtc, start).Set(x => x.EndUtc, end).Inc(x => x.Revision, 1)
                    .Push(x => x.History, new HistoryRecord
                    {
                        EventType = "ScheduleChangeApproved", Status = "Schedule change approved",
                        ReservationStatus = booking.Status, Actor = actor, AtUtc = now, Reason = change.Reason,
                        ScheduleChange = change, CommandId = commandId, CommandHash = hash
                    });
                var result = await Reservations.UpdateOneAsync(s, Builders<ReservationRecord>.Filter.Eq(x => x.Id, booking.Id)
                    & AtRevision(booking.Revision), update, cancellationToken: token);
                if (result.MatchedCount != 1) { throw new AllocationConflictException($"Booking {booking.Reference} changed."); }
                booking.StartUtc = start;
                booking.EndUtc = end;
            }

            // In this transaction all selected bookings now occupy their proposed intervals.
            foreach (var booking in bookings)
            {
                if (await HasAllocationConflictAsync(booking, s, token, now))
                {
                    throw new AllocationConflictException($"The assigned bays for {booking.Reference} are unavailable at the proposed time.");
                }
                await NotifyAsync(s, booking, $"change:{commandId}:Approved", "Schedule change approved", token);
            }
            return true;
        }, new TransactionOptions(readConcern: ReadConcern.Snapshot, writeConcern: WriteConcern.WMajority), timeout.Token);
    }
}
