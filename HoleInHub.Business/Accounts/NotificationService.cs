using HoleInHub.Data.Reservations;

namespace HoleInHub.Business.Accounts;

public sealed record QueuedEmail(string Id, string Recipient, string Subject, string Body, string? ReservationId, string? RequiredStatus, string? LeaseToken)
{
    public DateTime? ReminderStartUtc { get; init; }
    public DateTime? ExpiresUtc { get; init; }
    public string? VerificationId { get; init; }
    public DateTime? VerificationIssuedUtc { get; init; }
}
public sealed class NotificationService(OutboxPersistence persistence)
{
    public Task QueueAsync(string recipient, string subject, string body, CancellationToken ct) => persistence.EnqueueAsync(new OutboxRecord { Id = Guid.NewGuid().ToString("N"), Recipient = recipient, Subject = subject, Body = body, DueUtc = DateTime.UtcNow }, ct);
    public async Task<QueuedEmail?> ClaimAsync(CancellationToken ct)
    {
        var record = await persistence.ClaimAsync(DateTime.UtcNow, ct);
        return record is null ? null : new(record.Id, record.Recipient, record.Subject, record.Body, record.ReservationId, record.RequiredStatus, record.LeaseToken)
        {
            ReminderStartUtc = record.ReminderStartUtc,
            ExpiresUtc = record.ExpiresUtc,
            VerificationId = record.VerificationId,
            VerificationIssuedUtc = record.VerificationIssuedUtc
        };
    }
    public Task<bool> IsEligibleAsync(QueuedEmail email, CancellationToken ct) => persistence.IsEligibleAsync(new OutboxRecord
    {
        Id = email.Id, Recipient = email.Recipient, ReservationId = email.ReservationId, RequiredStatus = email.RequiredStatus,
        ReminderStartUtc = email.ReminderStartUtc, ExpiresUtc = email.ExpiresUtc,
        VerificationId = email.VerificationId, VerificationIssuedUtc = email.VerificationIssuedUtc
    }, ct);
    public Task CompleteAsync(QueuedEmail email, CancellationToken ct) => persistence.CompleteAsync(email.Id, email.LeaseToken!, ct);
    public Task SuppressAsync(QueuedEmail email, CancellationToken ct) => persistence.SuppressAsync(email.Id, email.LeaseToken!, DateTime.UtcNow, ct);
    public Task RetryAsync(QueuedEmail email, CancellationToken ct, string failureType) => persistence.RetryAsync(email.Id, email.LeaseToken!, DateTime.UtcNow, ct, failureType);
}
