using HoleInHub.Data.Services;
using System.Security.Cryptography;
using System.Text.Json;

namespace HoleInHub.Business.Reservations;

public sealed partial class ReservationService
{
    public async Task<BookingPriceQuote> GetQuoteAsync(DateOnly date, TimeOnly startTime, int durationMinutes,
        string service, string? serviceEntryId, int bayCount, bool demoAllowed, CancellationToken ct)
    {
        if (service == "WholeShop" && (!demoAllowed || !effectivePolicy.DemoPaymentsEnabled))
        {
            throw new ReservationValidationException("Whole-shop demo bookings are unavailable.");
        }
        var record = await ResolveBookingServiceAsync(service, serviceEntryId, ct);
        PreviewServiceInterval(record, date, startTime, durationMinutes, service);
        return PriceBooking(record, service, date, startTime, durationMinutes, bayCount);
    }

    public static BookingPriceQuote PriceBooking(ServiceCatalogRecord? record, string service, DateOnly date,
        TimeOnly startTime, int durationMinutes, int bayCount) => PricingRules.Calculate(service,
            record?.Pricing ?? PricingRules.Default(service), date, startTime, durationMinutes, bayCount,
            record?.Revision ?? 0, record?.DepositType ?? "None", record?.DepositValue ?? 0);

    public static string QuoteKey(BookingPriceQuote quote, string service, string? entryId, DateOnly date,
        TimeOnly time, int duration, int bayCount) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { quote, service, entryId, date, time, duration, bayCount })));

    private static BookingPriceQuote ValidateQuote(ServiceCatalogRecord? entry, SubmitReservationRequest request)
    {
        var quote = PriceBooking(entry, request.Service, request.Date, request.StartTime, request.DurationMinutes, request.ResourceIds.Length);
        var key = QuoteKey(quote, request.Service, request.ServiceEntryId, request.Date, request.StartTime, request.DurationMinutes, request.ResourceIds.Length);
        if (!string.IsNullOrEmpty(request.QuoteKey) && !string.Equals(request.QuoteKey, key, StringComparison.Ordinal))
        { throw new ReservationValidationException("The quote changed. Review the updated price and submit again."); }
        return quote;
    }
}
