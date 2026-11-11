using MongoDB.Bson;
using MongoDB.Driver;

namespace HoleInHub.Data.Reservations;

public sealed partial class ReservationPersistence
{
    public async Task RequestScheduleChangeAsync(string id, long revision, ScheduleChangeRecord proposal, string actor,
        Func<ReservationRecord, bool> eligible, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        ct = timeout.Token;
        using var session = await context.Client.StartSessionAsync(cancellationToken: ct);
        await session.WithTransactionAsync(async (s, token) =>
        {
            var booking = await Reservations.Find(s, x => x.Id == id).FirstOrDefaultAsync(token)
                ?? throw new AllocationConflictException("This booking is no longer available.");
            await GuardAsync(s, booking, token);
            var prior = booking.History.FirstOrDefault(x => x.CommandId == proposal.Id);
            if (prior is not null)
            {
                if (prior.CommandHash != proposal.PayloadHash) { throw new AllocationConflictException("This form was already submitted with different details. Reload the booking."); }
                return true;
            }
            if (booking.Revision != revision || !eligible(booking) || booking.ScheduleChange?.Status == "Pending")
            {
                throw new AllocationConflictException("The booking changed or already has a request. Reload its latest details.");
            }
            proposal.BaseRevision = revision + 1;
            proposal.OriginalStartUtc = booking.StartUtc;
            proposal.OriginalEndUtc = booking.EndUtc;
            proposal.OriginalResourceIds = booking.ResourceIds;
            proposal.ResourceIds = booking.ResourceIds;
            proposal.ResourceNames = booking.ResourceNames;
            var update = Builders<ReservationRecord>.Update.Set(x => x.ScheduleChange, proposal).Inc(x => x.Revision, 1)
                .Push(x => x.History, new HistoryRecord
                {
                    EventType = "ScheduleChangeRequested",
                    Status = "Schedule change requested",
                    ReservationStatus = booking.Status,
                    Actor = actor,
                    AtUtc = proposal.RequestedUtc,
                    Reason = proposal.Reason,
                    ScheduleChange = proposal,
                    CommandId = proposal.Id,
                    CommandHash = proposal.PayloadHash
                });
            await Reservations.UpdateOneAsync(s, Builders<ReservationRecord>.Filter.Eq(x => x.Id, id) & AtRevision(revision), update, cancellationToken: token);
            return true;
        }, new TransactionOptions(readConcern: ReadConcern.Snapshot, writeConcern: WriteConcern.WMajority), ct);
    }

    public async Task<(string Reference, DateTime StartUtc, DateTime EndUtc)> DecideScheduleChangeAsync(string id, string proposalId, long revision, bool approve,
        string[] resources, string actor, string reason, DateTime now, CancellationToken ct, ScheduleChangeRecord? directMove = null)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        using var session = await context.Client.StartSessionAsync(cancellationToken: timeout.Token);
        return await session.WithTransactionAsync(async (s, token) =>
        {
            var booking = await Reservations.Find(s, x => x.Id == id).FirstOrDefaultAsync(token)
                ?? throw new AllocationConflictException("This booking is no longer available.");
            var prior = booking.History.FirstOrDefault(entry => entry.CommandId == proposalId);
            if (directMove is not null && prior is not null)
            {
                if (prior.CommandHash != directMove.PayloadHash) { throw new AllocationConflictException("This move was submitted with different details. Reload the schedule."); }
                return (booking.Reference, booking.StartUtc, booking.EndUtc);
            }
            if (directMove is not null && booking.ScheduleChange?.Status == "Pending")
            {
                throw new AllocationConflictException("This booking has a pending change. Open its details to review it.");
            }
            var proposal = directMove is null ? booking.ScheduleChange : new ScheduleChangeRecord
            {
                Id = directMove.Id,
                PayloadHash = directMove.PayloadHash,
                RequestedUtc = now,
                StartUtc = directMove.StartUtc,
                EndUtc = directMove.EndUtc,
                Reason = reason,
                BaseRevision = revision,
                OriginalStartUtc = booking.StartUtc,
                OriginalEndUtc = booking.EndUtc,
                OriginalResourceIds = booking.ResourceIds,
                ResourceIds = booking.ResourceIds,
                ResourceNames = booking.ResourceNames
            };
            if (proposal is null || proposal.Id != proposalId || proposal.Status != "Pending")
            {
                throw new AllocationConflictException("This request was already decided or replaced. Reload the booking.");
            }
            // Every allocation writer takes the catalog guard before resource/date guards.
            var guards = context.Database.GetCollection<BsonDocument>("allocation_guards");
            await guards.UpdateOneAsync(s, new BsonDocument("_id", "catalog"), new BsonDocument("$inc", new BsonDocument("version", 1)),
                new UpdateOptions { IsUpsert = true }, token);
            if (booking.Revision != revision)
            {
                throw new AllocationConflictException("Another staff member updated this booking. Reload before deciding.");
            }
            if (approve)
            {
                if (proposal.BaseRevision != revision || booking.Status != "Confirmed" || booking.CancellationRequested
                    || booking.Service is not ("Regular" or "VIP") || now >= booking.StartUtc || now >= proposal.StartUtc)
                {
                    throw new AllocationConflictException("The booking is no longer eligible for this change. Decline the request or reload its latest details.");
                }
                var catalog = await Resources.Find(s, x => x.Active && x.Service == booking.Service).ToListAsync(token);
                if (resources.Length != booking.ResourceIds.Length || resources.Distinct().Count() != resources.Length
                    || resources.Any(resource => !catalog.Any(bay => bay.Id == resource)))
                {
                    throw new AllocationConflictException("Choose the same number of active bays for this service.");
                }
                var oldDay = TimeZoneInfo.ConvertTimeFromUtc(booking.StartUtc, TimeZoneInfo.FindSystemTimeZoneById("Asia/Manila")).ToString("yyyy-MM-dd");
                var newDay = TimeZoneInfo.ConvertTimeFromUtc(proposal.StartUtc, TimeZoneInfo.FindSystemTimeZoneById("Asia/Manila")).ToString("yyyy-MM-dd");
                var keys = booking.ResourceIds.Select(resource => $"{resource}:{oldDay}")
                    .Concat(resources.Select(resource => $"{resource}:{newDay}")).Distinct().Order();
                foreach (var key in keys)
                {
                    await guards.UpdateOneAsync(s, new BsonDocument("_id", key), new BsonDocument("$inc", new BsonDocument("version", 1)),
                        new UpdateOptions { IsUpsert = true }, token);
                }
                booking.StartUtc = proposal.StartUtc;
                booking.EndUtc = proposal.EndUtc;
                booking.ResourceIds = resources;
                booking.ResourceNames = catalog.Where(bay => resources.Contains(bay.Id)).ToDictionary(bay => bay.Id, bay => bay.Name);
                if (await HasReviewConflictAsync(booking, token, s, now))
                {
                    throw new AllocationConflictException("The proposed bays are unavailable. The original booking is unchanged.");
                }
                proposal.ResourceIds = resources;
                proposal.ResourceNames = booking.ResourceNames;
            }
            proposal.Status = approve ? "Approved" : "Declined";
            proposal.DecidedUtc = now;
            var update = Builders<ReservationRecord>.Update.Set(x => x.ScheduleChange, proposal).Inc(x => x.Revision, 1)
                .Push(x => x.History, new HistoryRecord
                {
                    EventType = approve ? "ScheduleChangeApproved" : "ScheduleChangeDeclined",
                    Status = approve ? "Schedule change approved" : "Schedule change declined",
                    ReservationStatus = booking.Status,
                    AtUtc = now,
                    Actor = actor,
                    Reason = reason,
                    ScheduleChange = proposal,
                    CommandId = directMove?.Id,
                    CommandHash = directMove?.PayloadHash
                });
            if (approve)
            {
                update = update.Set(x => x.StartUtc, booking.StartUtc).Set(x => x.EndUtc, booking.EndUtc)
                    .Set(x => x.ResourceIds, booking.ResourceIds).Set(x => x.ResourceNames, booking.ResourceNames);
                if (booking.Participants.Length > 0)
                {
                    foreach (var participant in booking.Participants)
                    {
                        participant.ResourceId = resources[Array.IndexOf(proposal.OriginalResourceIds, participant.ResourceId)];
                    }
                    update = update.Set(x => x.Participants, booking.Participants);
                }
            }
            await Reservations.UpdateOneAsync(s, Builders<ReservationRecord>.Filter.Eq(x => x.Id, id) & AtRevision(revision), update, cancellationToken: token);
            await NotifyAsync(s, booking, $"change:{proposal.Id}:{proposal.Status}", approve ? "Schedule change approved" : "Schedule change declined", token);
            return (booking.Reference, booking.StartUtc, booking.EndUtc);
        }, new TransactionOptions(readConcern: ReadConcern.Snapshot, writeConcern: WriteConcern.WMajority), timeout.Token);
    }
}
