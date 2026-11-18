using HoleInHub.Business.Accounts;
using HoleInHub.Business.Services;
using HoleInHub.Data.Reservations;

namespace HoleInHub.Business.Reservations;

public sealed record CustomerNotification(string Id, string BookingId, string Reference, DateTime AtUtc, bool Unread, string Title, string Message, string Icon);
public sealed record CustomerNotificationPage(int Page, bool HasMore, IReadOnlyList<CustomerNotification> Items);

public sealed partial class ReservationService
{
    public const int CustomerNotificationPageSize = 25;
    private static readonly Dictionary<string, (string Title, string Message, string Icon)> CustomerEvents = new()
    {
        ["SubmissionCreated"] = ("Request received", "We’ll let you know when your booking is reviewed.", "pending"),
        ["ReservationConfirmed"] = ("Reservation confirmed", "Your bay is reserved. View your booking for the session details.", "confirmed"),
        ["ReservationRejected"] = ("Request declined", "This booking request could not be accepted.", "alert"),
        ["ReservationExpired"] = ("Request expired", "The request expired before it could be confirmed.", "pending"),
        ["ReservationCancelled"] = ("Reservation cancelled", "The reservation has been cancelled.", "blocked"),
        ["CancellationDeclined"] = ("Cancellation declined", "Your reservation is still scheduled.", "alert"),
        ["ScheduleChangeApproved"] = ("Schedule updated", "View the latest time and bay details for your reservation.", "changes"),
        ["ScheduleChangeDeclined"] = ("Schedule change declined", "Your original reservation details still apply.", "alert"),
        ["ReservationNoShow"] = ("Reservation marked no-show", "Contact the venue if you believe this was a mistake.", "no-show"),
        ["ReservationExtended"] = ("Session extended", "View your reservation for the updated end time.", "pending"),
        ["DemoPaymentRecorded"] = ("Deposit status updated", "View your booking for the simulated deposit result.", "info")
    };
    private static readonly string[] CustomerEventTypes = CustomerEvents.Keys.ToArray();

    public async Task<CustomerNotificationPage> GetCustomerNotificationsAsync(string accountId, int page, CancellationToken ct)
    {
        page = Math.Clamp(page, 1, int.MaxValue / CustomerNotificationPageSize);
        var rows = await persistence.ReadCustomerNotificationsAsync(accountId, CustomerEventTypes, (page - 1) * CustomerNotificationPageSize, CustomerNotificationPageSize + 1, ct);
        var items = rows.Take(CustomerNotificationPageSize).Select(row =>
        {
            var (title, message, icon) = CustomerEvents[row.EventType];
            return new CustomerNotification($"{row.BookingId}:{row.Index}", row.BookingId, row.Reference, row.AtUtc, row.Unread, title, message, icon);
        }).ToArray();
        return new(page, rows.Length > CustomerNotificationPageSize, items);
    }

    public async Task<bool?> GetCustomerNotificationBadgeAsync(string accountId, CancellationToken ct)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(TimeSpan.FromMilliseconds(450));
        try { return await persistence.HasUnreadCustomerNotificationsAsync(accountId, CustomerEventTypes, budget.Token); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return null; }
        catch (Exception error) when (ServiceAvailability.Translate(error) is not null) { return null; }
    }

    public async Task<int> MarkCustomerNotificationsReadAsync(string accountId, NotificationReadRow[] rows, CancellationToken ct)
    {
        var requests = new List<CustomerNotificationRead>();
        foreach (var row in rows)
        {
            var parts = row.Id?.Split(':', 2);
            if (parts is not [var bookingId, var indexText] || bookingId.Length is 0 or > 100 || !int.TryParse(indexText, out var index) || index < 0
                || row.ActivityTicks <= 0 || row.ActivityTicks > DateTime.MaxValue.Ticks) { continue; }
            requests.Add(new(bookingId, index, new DateTime(row.ActivityTicks, DateTimeKind.Utc)));
        }
        if (requests.Count > 0) { await persistence.MarkCustomerNotificationsReadAsync(accountId, CustomerEventTypes, requests, ct); }
        return await persistence.HasUnreadCustomerNotificationsAsync(accountId, CustomerEventTypes, ct) ? 1 : 0;
    }
}
