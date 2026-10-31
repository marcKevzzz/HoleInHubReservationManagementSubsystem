using HoleInHub.Data.Reservations;

namespace HoleInHub.Business.Accounts;

public sealed record QueuedEmail(string Id, string Recipient, string Subject, string Body, string? ReservationId, string? RequiredStatus, string? LeaseToken);
public sealed class NotificationService(OutboxPersistence persistence)
{
    public Task QueueAsync(string recipient, string subject, string body, CancellationToken ct) => persistence.EnqueueAsync(new OutboxRecord { Id = Guid.NewGuid().ToString("N"), Recipient = recipient, Subject = subject, Body = body, DueUtc = DateTime.UtcNow }, ct);
    public async Task<QueuedEmail?> ClaimAsync(CancellationToken ct)
    {
        var record = await persistence.ClaimAsync(DateTime.UtcNow, ct);
        return record is null ? null : new(record.Id, record.Recipient, record.Subject, record.Body, record.ReservationId, record.RequiredStatus, record.LeaseToken);
    }
    public Task<bool> IsEligibleAsync(QueuedEmail email, CancellationToken ct) => persistence.IsEligibleAsync(new OutboxRecord { Id = email.Id, ReservationId = email.ReservationId, RequiredStatus = email.RequiredStatus }, ct);
    public Task CompleteAsync(QueuedEmail email, CancellationToken ct) => persistence.CompleteAsync(email.Id, email.LeaseToken!, ct);
    public Task RetryAsync(QueuedEmail email, CancellationToken ct) => persistence.RetryAsync(email.Id, email.LeaseToken!, DateTime.UtcNow, ct);
}
