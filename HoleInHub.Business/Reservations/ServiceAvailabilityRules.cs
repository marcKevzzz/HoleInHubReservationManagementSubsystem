using System.Globalization;
using HoleInHub.Data.Services;
using HoleInHub.Data.Reservations;

namespace HoleInHub.Business.Reservations;

public static class ServiceAvailabilityRules
{
    public static ServiceAvailabilityRule[] Defaults(string type) => type switch
    {
        "KpgaPro" => [new(2, null, 600, 1440, false, false), new(4, null, 600, 1440, false, false), new(6, null, 600, 1440, false, false)],
        "KpgaGroup" => [new(2, null, 900, 960, true, false)],
        _ => []
    };

    public static string Describe(IReadOnlyList<ServiceAvailabilityRule> rules)
    {
        if (rules.Count == 0) { return "Venue operating hours"; }
        var weekly = rules.Where(rule => rule.Weekday.HasValue).Select(rule => CultureInfo.GetCultureInfo("en").DateTimeFormat.GetAbbreviatedDayName((DayOfWeek)rule.Weekday!.Value)).Distinct();
        var parts = weekly.ToList();
        if (rules.Any(rule => rule.Date is not null)) { parts.Add("Specific dates"); }
        return string.Join(", ", parts);
    }

    public static void Validate(IReadOnlyList<ServiceAvailabilityRule> rules)
    {
        if (rules.Count > 120) { throw new ArgumentException("Use up to 120 availability rules."); }
        foreach (var rule in rules)
        {
            if (rule.Date is null ? rule.Weekday is null or < 0 or > 6 :
                rule.Weekday is not null || !DateOnly.TryParseExact(rule.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            { throw new ArgumentException("Choose a weekday or enter a specific date for each availability rule."); }
            if (rule.Closed && rule.Date is null) { throw new ArgumentException("A closure needs a specific date."); }
            if (!rule.Closed && (rule.StartMinute < 0 || rule.EndMinute > 1440 || rule.EndMinute <= rule.StartMinute ||
                rule.StartMinute % 30 != 0 || rule.EndMinute % 30 != 0))
            { throw new ArgumentException("Availability times must be between midnight and the next midnight in 30-minute increments."); }
        }
        foreach (var group in rules.GroupBy(rule => (rule.Weekday, rule.Date)))
        {
            var windows = group.OrderBy(rule => rule.StartMinute).ToArray();
            if (windows.Length > 1 && (windows.Any(rule => rule.Closed) ||
                windows.Zip(windows.Skip(1)).Any(pair => pair.First.EndMinute > pair.Second.StartMinute)))
            { throw new ArgumentException("Availability rules for the same day overlap. Adjust the hours or keep just the closure."); }
            if (windows.Any(rule => rule.FixedSession) && windows.Any(rule => !rule.FixedSession && !rule.Closed))
            { throw new ArgumentException("Choose either fixed sessions or bookable hours for the same day."); }
        }
    }

    public static bool Allows(IReadOnlyList<ServiceAvailabilityRule> rules, DateOnly date, TimeOnly time, int duration)
    {
        if (rules.Count == 0) { return true; }
        var key = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var overrides = rules.Where(rule => rule.Date == key).ToArray();
        var windows = overrides.Length > 0 ? overrides : rules.Where(rule => rule.Weekday == (int)date.DayOfWeek);
        var start = time.Hour * 60 + time.Minute;
        return windows.Any(rule => !rule.Closed && (rule.FixedSession
            ? rule.StartMinute == start && rule.EndMinute == start + duration
            : rule.StartMinute <= start && rule.EndMinute >= start + duration));
    }
}

public sealed partial class ReservationService
{
    public async Task<ServiceCatalogRecord?> ResolveBookingServiceAsync(string type, string? entryId, CancellationToken ct, bool existingBooking = false)
    {
        if (!Services.ServiceCatalog.BookingTypes.Contains(type))
        { throw new ReservationValidationException("Choose a listed experience."); }
        var explicitEntry = !string.IsNullOrEmpty(entryId);
        if (entryId is { Length: > 80 }) { throw new ReservationValidationException("Choose a listed experience."); }
        var entry = serviceCatalog is null ? null : await serviceCatalog.GetBookingServiceAsync(explicitEntry ? entryId! : type, ct);
        if (existingBooking && (entry is null || entry.Deleted || entry.BookingService != type)) { return null; }
        if (explicitEntry && (entry is null || !existingBooking && !entry.Active || entry.Deleted || entry.BookingService != type))
        { throw new ReservationValidationException("This service is no longer available. Choose another experience."); }
        if (!explicitEntry && entry is { BookingService: var bookingType } && bookingType != type) { return null; }
        if (!existingBooking && entry is not null && (!entry.Active || entry.Deleted))
        { throw new ReservationValidationException("This service is no longer available. Choose another experience."); }
        return entry;
    }

    public async Task<(DateTime Start, DateTime End)> PreviewBookingIntervalAsync(DateOnly date, TimeOnly time, int duration,
        string type, CancellationToken ct, string? entryId = null, bool existingBooking = false)
    {
        var entry = await ResolveBookingServiceAsync(type, entryId, ct, existingBooking);
        return PreviewServiceInterval(entry, date, time, duration, type);
    }

    public static (DateTime Start, DateTime End) PreviewServiceInterval(ServiceCatalogRecord? entry, DateOnly date, TimeOnly time, int duration, string type)
    {
        if (entry?.AvailabilityRules is not { } rules) { return ReservationRules.PreviewInterval(date, time, duration, type); }
        var interval = type == "WholeShop" ? ReservationRules.PreviewInterval(date, time, duration, type)
            : ReservationRules.Interval(date, time, duration);
        if (!ServiceAvailabilityRules.Allows(rules, date, time, duration))
        { throw new ReservationValidationException("Choose a session within this service’s available hours."); }
        return interval;
    }

    private static void ValidateChangedSession(ReservationRecord booking, ServiceCatalogRecord? service)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(booking.StartUtc, ReservationRules.Manila);
        var duration = (int)(booking.EndUtc - booking.StartUtc).TotalMinutes;
        PreviewServiceInterval(service?.BookingService == booking.Service ? service : null,
            DateOnly.FromDateTime(local), TimeOnly.FromDateTime(local), duration, booking.Service);
        ValidateServiceCapacity(service, booking.Attendees);
    }
}
