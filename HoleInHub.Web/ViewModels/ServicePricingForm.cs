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
        Day = row.Day, Date = row.Date, Rate = row.Rate,
        Start = TimeOnly.MinValue.AddMinutes(row.StartMinute), EndsAtMidnight = row.EndMinute == 1440,
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
        Day = row.Day, Date = row.Date, HalfDayRate = row.HalfDayRate, WholeDayRate = row.WholeDayRate
    };
}

public sealed partial class ServiceEditorForm
{
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
        PricingRules.Validate(BookingService, pricing);
        return pricing;
    }

    public ServiceEditorForm LoadPricing(ServicePricing pricing)
    {
        RateAmount = pricing.BaseRate;
        ClassCount = pricing.ClassCount;
        WholeDayRate = pricing.WholeDayRate;
        Bands = pricing.Bands.Select(PricingBandForm.From).ToList();
        RentalOverrides = pricing.RentalOverrides.Select(RentalPriceForm.From).ToList();
        return this;
    }
}
