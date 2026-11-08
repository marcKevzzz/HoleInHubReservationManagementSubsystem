using HoleInHub.Business.Accounts;

namespace HoleInHub.Business.Reservations;

public sealed record StaffCustomer(string BookingId, string FirstName, string LastName, string Email, string Phone,
    string Kind, string Reference, DateTime CreatedUtc);
public sealed record StaffCustomerDirectory(IReadOnlyList<StaffCustomer> Items, string Search, int Page, bool HasMore);
public sealed record StaffCustomerProfile(StaffCustomer Customer, IReadOnlyList<ReservationDto> Bookings, long Total,
    long Upcoming, long Completed, long Cancelled, bool UpcomingOnly, int Page, bool HasMore);

public sealed partial class ReservationService
{
    public async Task<StaffCustomerDirectory> SearchCustomersAsync(string? search, int page, CancellationToken ct)
    {
        var term = search?.Trim() ?? "";
        if (term.Length is 1 or > 100)
        {
            throw new ReservationValidationException("Enter at least 2 characters and no more than 100, or clear the search to see recent contacts.");
        }
        page = Math.Clamp(page, 1, 1000);
        var digits = ContactRules.PhoneDigits(term);
        string[] phones = [];
        if (digits.Length is >= 7 and <= 15)
        {
            phones = digits.Length == 12 && digits.StartsWith("63", StringComparison.Ordinal) ? [digits, "0" + digits[2..]]
                : digits.Length == 11 && digits.StartsWith('0') ? [digits, "63" + digits[1..]] : [digits];
        }
        var result = await persistence.SearchCustomersAsync(term, phones, page, ct);
        return new(result.Items.Select(row => Contact(row.Contact, row.Kind)).ToArray(), term, page, result.HasMore);
    }

    public async Task<StaffCustomerProfile?> GetCustomerProfileAsync(string bookingId, bool upcomingOnly, int page, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(bookingId) || bookingId.Length > 100) { return null; }
        page = Math.Clamp(page, 1, 1000);
        var result = await persistence.ReadCustomerProfileAsync(bookingId, upcomingOnly, page, (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime, ct);
        return result is null ? null : new(Contact(result.Customer.Contact, result.Customer.Kind), result.Bookings.Select(Map).ToArray(),
            result.Total, result.Upcoming, result.Completed, result.Cancelled, upcomingOnly, page, result.HasMore);
    }

    private static StaffCustomer Contact(HoleInHub.Data.Reservations.ReservationRecord record, string kind) =>
        new(record.Id, record.FirstName, record.LastName, record.Email, record.Phone, kind, record.Reference, record.CreatedUtc);
}
