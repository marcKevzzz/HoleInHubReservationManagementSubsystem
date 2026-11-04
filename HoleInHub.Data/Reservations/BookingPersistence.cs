using HoleInHub.Data.Context;
using MongoDB.Driver;

namespace HoleInHub.Data.Reservations;

public sealed partial class ReservationPersistence
{
    public Task<ReservationRecord?> ByIdAsync(string id, CancellationToken ct) => Reservations.Find(x => x.Id == id).FirstOrDefaultAsync(ct)!;
    public Task<ReservationRecord?> ByAccessAsync(string id, string hash, CancellationToken ct) => Reservations.Find(x => (id == "" || x.Id == id) && x.AccessTokenHash == hash).FirstOrDefaultAsync(ct)!;
    public async Task<IReadOnlyList<ReservationRecord>> ReviewQueueAsync(CancellationToken ct) =>
        await Reservations.Find(x => x.Status == "Pending" || x.CancellationRequested || x.PaymentStatus == "LatePayment").SortBy(x => x.ReviewDeadlineUtc).Limit(200).ToListAsync(ct);

    private Task NotifyAsync(IClientSessionHandle session, ReservationRecord record, string eventName, string subject, CancellationToken ct, string? encryptedBody = null) =>
        context.Database.GetCollection<OutboxRecord>("outbox").InsertOneAsync(session, new OutboxRecord
        {
            Id = $"{record.Id}:{eventName}",
            Recipient = record.Email,
            Subject = subject,
            Body = encryptedBody ?? $"Reservation {record.Reference}: {record.Status}. Session {record.StartUtc:O}. Review/payment deadline {record.ReviewDeadlineUtc:O}. Keep your booking status link to view updates.",
            ReservationId = record.Id,
            RequiredStatus = eventName == "submitted" ? null : record.Status,
            DueUtc = DateTime.UtcNow
        }, cancellationToken: ct);

    public async Task<bool> TransitionAsync(string id, string action, string actor, DateTime now, string[] staffEmails, CancellationToken ct, string reason = "")
    {
        using var session = await context.Client.StartSessionAsync(cancellationToken: ct);
        return await session.WithTransactionAsync(async (s, token) =>
        {
            var record = await Reservations.Find(s, x => x.Id == id).FirstOrDefaultAsync(token);
            if (record == null)
            {
                return false;
            }

            await GuardAsync(s, record, token);
            // The guard makes expiry, payment, approval and cancellation compete on the same allocation.
            var update = Builders<ReservationRecord>.Update;
            var pending = record.Status == "Pending" && record.ReviewDeadlineUtc > now;
            UpdateDefinition<ReservationRecord> change;
            string history;
            switch (action)
            {
                case "approve" when pending && record.Service != "WholeShop" && (!(record.Service is "PhilippineCoach" or "KpgaPro" or "KpgaGroup") || record.CoachConfirmed):
                    record.Status = "Confirmed"; history = "Confirmed"; change = update.Set(x => x.Status, record.Status); break;
                case "reject" when pending:
                    record.Status = "Rejected"; history = "Rejected"; change = update.Set(x => x.Status, record.Status).Set(x => x.CancellationRequested, false); break;
                case "coach" when pending && record.Service is "PhilippineCoach" or "KpgaPro" or "KpgaGroup":
                    if (record.CoachConfirmed)
                    {
                        return true;
                    }

                    history = "Coach confirmed"; change = update.Set(x => x.CoachConfirmed, true); break;
                case "request-cancellation" when (record.Status is "Pending" or "Confirmed") && record.StartUtc >= now.AddHours(1):
                    if (record.CancellationRequested)
                    {
                        return true;
                    }

                    history = "Cancellation requested"; change = update.Set(x => x.CancellationRequested, true); break;
                case "cancel" when record.CancellationRequested && record.Status is "Pending" or "Confirmed":
                    record.Status = "Cancelled"; history = "Cancelled"; change = update.Set(x => x.Status, record.Status).Set(x => x.CancellationRequested, false); break;
                case "keep" when record.CancellationRequested && record.Status is "Pending" or "Confirmed":
                    history = "Cancellation declined"; change = update.Set(x => x.CancellationRequested, false); break;
                default: return false;
            }
            var eventType = action switch
            {
                "approve" => "ReservationConfirmed",
                "reject" => "ReservationRejected",
                "coach" => "CoachConfirmed",
                "request-cancellation" => "CancellationRequested",
                "cancel" => "ReservationCancelled",
                "keep" => "CancellationDeclined",
                _ => throw new InvalidOperationException("Unsupported reservation action.")
            };
            change = change.Push(x => x.History, new HistoryRecord { EventType = eventType, ReservationStatus = record.Status, Status = history, Actor = actor, AtUtc = now, Reason = reason });
            await Reservations.UpdateOneAsync(s, x => x.Id == id, change, cancellationToken: token);
            await NotifyAsync(s, record, $"action:{record.History.Count}:{action}", history, token);
            if (action == "request-cancellation")
            {
                foreach (var email in staffEmails.Distinct())
                {
                    await context.Database.GetCollection<OutboxRecord>("outbox").InsertOneAsync(s, new OutboxRecord { Id = $"{id}:cancel:{record.History.Count}:{email}", Recipient = email, Subject = "Cancellation review needed", Body = $"Review cancellation for {record.Reference}. Its allocation remains held.", DueUtc = now }, cancellationToken: token);
                }
            }
            return true;
        }, new TransactionOptions(readConcern: ReadConcern.Snapshot, writeConcern: WriteConcern.WMajority), ct);
    }

    public async Task<bool> ApplyDemoPaymentAsync(string id, string outcome, string reference, DateTime now, CancellationToken ct)
    {
        using var session = await context.Client.StartSessionAsync(cancellationToken: ct);
        return await session.WithTransactionAsync(async (s, token) =>
        {
            var record = await Reservations.Find(s, x => x.Id == id && x.Service == "WholeShop" && x.PolicyVersion >= 1).FirstOrDefaultAsync(token);
            if (record == null)
            {
                return false;
            }

            await GuardAsync(s, record, token);
            if (record.PaymentStatus == "DepositReceived")
            {
                return true;
            }

            if (record.PaymentStatus == "LatePayment")
            {
                return false;
            }

            var eligible = record.Status == "Pending" && record.ReviewDeadlineUtc > now;
            var paid = outcome == "success";
            var status = paid ? (eligible ? "DepositReceived" : "LatePayment") : outcome == "failure" ? "Failed" : "CheckoutCancelled";
            if (!eligible && !paid)
            {
                return false;
            }

            var change = Builders<ReservationRecord>.Update.Set(x => x.PaymentStatus, status);
            if (paid)
            {
                change = change.Set(x => x.PaymentReference, reference);
                if (eligible) { record.Status = "Confirmed"; change = change.Set(x => x.Status, "Confirmed"); }
                else if (record.Status == "Pending") { record.Status = "Expired"; change = change.Set(x => x.Status, "Expired"); }
            }
            change = change.Push(x => x.History, new HistoryRecord { EventType = "DemoPaymentRecorded", ReservationStatus = record.Status, Status = "Demo payment: " + status, AtUtc = now, Actor = "Demo POS" });
            await Reservations.UpdateOneAsync(s, x => x.Id == id, change, cancellationToken: token);
            if (paid)
            {
                await NotifyAsync(s, record, status, eligible ? "Reservation confirmed — deposit recorded" : "Late payment requires reconciliation", token);
            }

            return eligible;
        }, new TransactionOptions(readConcern: ReadConcern.Snapshot, writeConcern: WriteConcern.WMajority), ct);
    }
}

public sealed class EmailVerificationPersistence(IMongoDbContext context)
{
    private IMongoCollection<GuestVerificationRecord> Records => context.Database.GetCollection<GuestVerificationRecord>("guest_verifications");
    public async Task<bool> IssueAsync(GuestVerificationRecord record, OutboxRecord email, CancellationToken ct, EmailDeliveryQuota? quota = null)
    {
        using var session = await context.Client.StartSessionAsync(cancellationToken: ct);
        try
        {
            return await session.WithTransactionAsync(async (s, token) =>
            {
                if (quota is not null)
                {
                    var limits = context.Database.GetCollection<RecoveryDeliveryRecord>("guest_recovery_quotas");
                    var guard = await limits.FindOneAndUpdateAsync(s, Builders<RecoveryDeliveryRecord>.Filter.Eq(x => x.Id, quota.Id),
                        Builders<RecoveryDeliveryRecord>.Update.Inc(x => x.Version, 1).SetOnInsert(x => x.IssuedUtc, []).SetOnInsert(x => x.ExpiresUtc, record.SentUtc + quota.Window),
                        new FindOneAndUpdateOptions<RecoveryDeliveryRecord> { IsUpsert = true, ReturnDocument = ReturnDocument.After }, token);
                    var recent = guard.IssuedUtc.Where(x => x > record.SentUtc - quota.Window).ToList();
                    if (recent.Count >= quota.Limit || recent.Any(x => x > record.SentUtc - quota.Cooldown)) { return false; }
                    recent.Add(record.SentUtc);
                    await limits.UpdateOneAsync(s, x => x.Id == quota.Id, Builders<RecoveryDeliveryRecord>.Update
                        .Set(x => x.IssuedUtc, recent).Set(x => x.ExpiresUtc, record.SentUtc + quota.Window), cancellationToken: token);
                }
                var prior = await Records.Find(s, x => x.Id == record.Id).FirstOrDefaultAsync(token);
                if (prior != null && prior.SentUtc > record.SentUtc.AddMinutes(-1))
                {
                    return false;
                }

                await Records.ReplaceOneAsync(s, x => x.Id == record.Id, record, new ReplaceOptions { IsUpsert = true }, token);
                await context.Database.GetCollection<OutboxRecord>("outbox").InsertOneAsync(s, email, cancellationToken: token);
                return true;
            }, new TransactionOptions(readConcern: ReadConcern.Snapshot, writeConcern: WriteConcern.WMajority), ct);
        }
        catch (MongoWriteException e) when (e.WriteError.Category == ServerErrorCategory.DuplicateKey) { return false; }
    }
    public async Task<bool> VerifyAsync(string id, string email, string hash, DateTime now, CancellationToken ct, string? purpose = null, bool consume = false)
    {
        var record = await Records.FindOneAndUpdateAsync(x => x.Id == id && x.Email == email && (purpose == null || x.Purpose == purpose) && x.ExpiresUtc > now && x.Attempts < 5 && x.VerifiedUtc == null && x.ConsumedBySubmissionKey == null,
            Builders<GuestVerificationRecord>.Update.Inc(x => x.Attempts, 1), new FindOneAndUpdateOptions<GuestVerificationRecord> { ReturnDocument = ReturnDocument.After }, ct);
        if (record == null || record.CodeHash != hash)
        {
            return false;
        }

        var update = Builders<GuestVerificationRecord>.Update.Set(x => x.VerifiedUtc, now);
        if (consume) { update = update.Set(x => x.ConsumedBySubmissionKey, "GuestRecovery"); }
        var result = await Records.UpdateOneAsync(x => x.Id == id && x.Email == email && (purpose == null || x.Purpose == purpose) && x.CodeHash == hash && x.VerifiedUtc == null && x.ExpiresUtc > now && x.ConsumedBySubmissionKey == null,
            update, cancellationToken: ct);
        return result.ModifiedCount == 1;
    }
    public Task<GuestVerificationRecord?> FindAsync(string id, CancellationToken ct) => Records.Find(x => x.Id == id).FirstOrDefaultAsync(ct)!;
}
