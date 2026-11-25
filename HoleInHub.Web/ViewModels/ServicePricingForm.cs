using System.Globalization;
using HoleInHub.Business.Reservations;
using HoleInHub.Data.Services;

namespace HoleInHubReservationManagementSubsystem.ViewModels;

public sealed class PricingBandForm
{
    public DayOfWeek? Day { get; set; }
    public DateOnly? Date { get; set; }
    public TimeOnly? Start { get; set; }
    public TimeOnly? End { get; set; }
    public bool EndsAtMidnight { get; set; }
    public decimal Rate { get; set; }

    public HourlyRateBand ToBand() => new(Day, Date, Minute(Start), EndsAtMidnight ? 1440 : Minute(End), Rate);

    public static PricingBandForm From(HourlyRateBand row) => new()
    {
        Day = row.Day,
        Date = row.Date,
        Rate = row.Rate,
        Start = TimeOnly.MinValue.AddMinutes(row.StartMinute),
        EndsAtMidnight = row.EndMinute == 1440,
        End = row.EndMinute == 1440 ? null : TimeOnly.MinValue.AddMinutes(row.EndMinute)
    };

    private static int Minute(TimeOnly? time) => time is { } value && value.Ticks % TimeSpan.TicksPerMinute == 0
        ? value.Hour * 60 + value.Minute : -1;
}

public sealed class RentalPriceForm
{
    public DayOfWeek? Day { get; set; }
    public DateOnly? Date { get; set; }
    public decimal HalfDayRate { get; set; }
    public decimal WholeDayRate { get; set; }

    public RentalRateOverride ToOverride() => new(Day, Date, HalfDayRate, WholeDayRate);
    public static RentalPriceForm From(RentalRateOverride row) => new()
    {
        Day = row.Day,
        Date = row.Date,
        HalfDayRate = row.HalfDayRate,
        WholeDayRate = row.WholeDayRate
    };
}

public sealed partial class ServiceEditorForm
{
    public decimal PremiumRentalRate { get; set; }
    public string? RentalHolidays { get; set; }
    public bool EditPricing { get; set; }
    public int ClassCount { get; set; }
    public decimal WholeDayRate { get; set; }
    public List<PricingBandForm> Bands { get; set; } = [];
    public List<RentalPriceForm> RentalOverrides { get; set; } = [];

    public ServicePricing PricingRulesFromForm()
    {
        var pricing = new ServicePricing
        {
            BaseRate = RateAmount ?? 0m,
            ClassCount = PricingRules.IsPackage(BookingService) ? ClassCount : 0,
            WholeDayRate = BookingService == "WholeShop" ? WholeDayRate : 0,
            Bands = BookingService != "WholeShop" && !PricingRules.IsPackage(BookingService) ? Bands.Select(row => row.ToBand()).ToArray() : [],
            RentalOverrides = BookingService == "WholeShop" ? RentalOverrides.Select(row => row.ToOverride()).ToArray() : []
        };
        if (SeasonalRental.IsPackage(Id))
        {
            var overrides = new List<RentalRateOverride>();
            foreach (var day in new[] { DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday })
            { overrides.Add(new(day, null, PremiumRentalRate, PremiumRentalRate)); }
            foreach (var value in (RentalHolidays ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                if (!DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                { throw new ArgumentException("Enter holiday dates as YYYY-MM-DD, separated by commas."); }
                overrides.Add(new(null, date, PremiumRentalRate, PremiumRentalRate));
            }
            pricing = new() { BaseRate = RateAmount ?? 0, WholeDayRate = RateAmount ?? 0, RentalOverrides = overrides.ToArray() };
        }
        PricingRules.Validate(BookingService, pricing);
        return pricing;
    }

    public ServiceEditorForm LoadPricing(ServicePricing pricing)
    {
        PremiumRentalRate = pricing.RentalOverrides.FirstOrDefault(row => row.Day == DayOfWeek.Friday)?.HalfDayRate ?? 0;
        RentalHolidays = string.Join(", ", pricing.RentalOverrides.Where(row => row.Date.HasValue).Select(row => row.Date!.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
        RateAmount = pricing.BaseRate;
        ClassCount = pricing.ClassCount;
        WholeDayRate = pricing.WholeDayRate;
        Bands = pricing.Bands.Select(PricingBandForm.From).ToList();
        RentalOverrides = pricing.RentalOverrides.Select(RentalPriceForm.From).ToList();
        return this;
    }
}
