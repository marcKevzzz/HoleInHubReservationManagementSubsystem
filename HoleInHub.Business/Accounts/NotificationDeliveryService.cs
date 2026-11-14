using System.Security.Cryptography;
using System.Text;
using HoleInHub.Data.Accounts;
using HoleInHub.Data.Reservations;

namespace HoleInHub.Business.Accounts;

public sealed record NotificationDeliveryItem(string Id, string Recipient, string Subject, string Status, int Attempts,
    DateTime DueUtc, DateTime? LastAttemptUtc, DateTime? FinishedUtc, string? Failure, bool CanRetry, bool Unread, long ActivityTicks);

public sealed record NotificationReadRow(string Id, long ActivityTicks);

public sealed record NotificationDeliveryPage(string Status, int Page, IReadOnlyList<NotificationDeliveryItem> Items, bool HasMore, long Unread);

public sealed class NotificationDeliveryService(OutboxPersistence outbox, AccountPersistence accounts, TimeProvider clock)
{
    public static IReadOnlyList<string> Filters { get; } = ["all", "queued", "sending", "sent", "failed", "suppressed"];
    public const int PageSize = 20;

    private static string AdminKey(string accountId) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(accountId)));

    private async Task<DateTime> ReadThroughAsync(string accountId, CancellationToken ct) =>
        await accounts.GetNotificationReadTimeAsync(accountId, ct) ?? DateTime.UnixEpoch;

    public async Task<long> CountUnreadAsync(string accountId, CancellationToken ct) =>
        await outbox.CountUnreadDeliveryAsync(AdminKey(accountId), await ReadThroughAsync(accountId, ct), ct);

    public async Task<long> MarkRowsReadAsync(string accountId, IReadOnlyList<NotificationReadRow> rows, CancellationToken ct)
    {
        var read = new Dictionary<string, DateTime>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            if (!ValidId(row.Id) || row.ActivityTicks <= DateTime.UnixEpoch.Ticks || row.ActivityTicks > DateTime.MaxValue.Ticks) { continue; }
            var through = new DateTime(row.ActivityTicks, DateTimeKind.Utc);
            if (through > read.GetValueOrDefault(row.Id)) { read[row.Id] = through; }
        }
        if (read.Count > 0) { await outbox.MarkDeliveryRowsReadAsync(AdminKey(accountId), read, ct); }
        return await CountUnreadAsync(accountId, ct);
    }

    public async Task<NotificationDeliveryPage> ListAsync(string accountId, string? status, int page, CancellationToken ct)
    {
        status = Filters.Contains(status) ? status! : "all";
        page = Math.Clamp(page, 1, 1000);
        var now = clock.GetUtcNow().UtcDateTime;
        var since = await ReadThroughAsync(accountId, ct);
        var adminKey = AdminKey(accountId);
        var rows = await outbox.ListDeliveryAsync(status, (page - 1) * PageSize, PageSize + 1, now, ct);
        var unread = await outbox.CountUnreadDeliveryAsync(adminKey, since, ct);
        return new(status, page, rows.Take(PageSize).Select(row => Map(row, now, since, adminKey)).ToArray(),
            page < 1000 && rows.Count > PageSize, unread);
    }

    public Task<bool> RetryAsync(string? id, CancellationToken ct) =>
        ValidId(id) ? outbox.RetryDeliveryAsync(id!, clock.GetUtcNow().UtcDateTime, ct) : Task.FromResult(false);

    private static bool ValidId(string? id) => !string.IsNullOrWhiteSpace(id) && id.Length <= 512 && !id.Any(char.IsControl);

    private static NotificationDeliveryItem Map(NotificationDeliveryRow row, DateTime now, DateTime since, string adminKey)
    {
        var status = row.SentUtc is not null ? "Sent" : row.SuppressedUtc is not null ? "Suppressed" :
            row.Failed ? "Failed" : row.LeaseUntilUtc > now ? "Sending" : "Queued";
        var failure = row.FailureType switch
        {
            "Timeout" => "Delivery timed out",
            "ContentUnavailable" => "Message content unavailable",
            "AttemptsExhausted" => "Attempts exhausted",
            "DeliveryFailure" => "Delivery failed",
            _ => null
        };
        var activity = new[] { row.DueUtc, row.LastAttemptUtc, row.SentUtc, row.FailedUtc, row.SuppressedUtc }.Max()!.Value;
        var rowReadThrough = row.ReadThroughByAdmin?.GetValueOrDefault(adminKey);
        return new(row.Id, row.Recipient, row.Subject, status, row.Attempts, row.DueUtc, row.LastAttemptUtc,
            row.SentUtc ?? row.SuppressedUtc ?? row.FailedUtc, failure,
            status == "Failed" && row.LeaseUntilUtc.GetValueOrDefault() <= now &&
            !row.ProtectedWithoutExpiry && (row.ExpiresUtc is null || row.ExpiresUtc > now),
            activity > since && (rowReadThrough is null || activity > rowReadThrough), activity.Ticks);
    }
}
