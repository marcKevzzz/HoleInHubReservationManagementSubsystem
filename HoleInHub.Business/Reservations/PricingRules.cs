using System.Globalization;
using HoleInHub.Data.Services;

namespace HoleInHub.Business.Reservations;

public sealed record PricingLine(string Description, decimal Amount);

public sealed record BookingPriceQuote(decimal Total, decimal DepositAmount, long Revision, IReadOnlyList<PricingLine> Lines, string Currency = "PHP");

public sealed class PricingValidationException(string field, string message) : ArgumentException(message)
{
    public string Field { get; } = field;
}

public static class PricingRules
{
    public static bool IsPackage(string service) => service is "KpgaPro" or "KpgaGroup";

    public static ServicePricing Default(string service) => service switch
    {
        "Regular" => new()
        {
            BaseRate = 600m,
            Bands = Enum.GetValues<DayOfWeek>().Select(day => new HourlyRateBand(day, null,
                day is DayOfWeek.Saturday or DayOfWeek.Sunday ? 0 : 900, 1440, 800m)).ToArray()
        },
        "VIP" => new() { BaseRate = 1000m },
        "PhilippineCoach" => new() { BaseRate = 2000m },
        "KpgaPro" => new() { BaseRate = 15000m, ClassCount = 8 },
        "KpgaGroup" => new() { BaseRate = 2500m, ClassCount = 4 },
        "WholeShop" => new()
        {
            BaseRate = 40000m, WholeDayRate = 60000m,
            RentalOverrides = [new(DayOfWeek.Friday, null, 50000m, 80000m),
                new(DayOfWeek.Saturday, null, 50000m, 80000m), new(DayOfWeek.Sunday, null, 50000m, 80000m)]
        },
        _ => throw new ReservationValidationException("Choose a listed experience.")
    };

    public static void Validate(string service, ServicePricing pricing)
    {
        ValidateMoney("BaseRate", pricing.BaseRate);
        if (IsPackage(service))
        {
            if (pricing.ClassCount < 1)
            {
                throw new PricingValidationException("ClassCount", "Enter at least one included class.");
            }
            return;
        }

        if (service == "WholeShop")
        {
            ValidateMoney("WholeDayRate", pricing.WholeDayRate);
            var seen = new HashSet<(DayOfWeek?, DateOnly?)>();
            for (var index = 0; index < pricing.RentalOverrides.Length; index++)
            {
                var row = pricing.RentalOverrides[index];
                var field = $"RentalOverrides[{index}]";
                ValidateDay(field, row.Day, row.Date);
                ValidateMoney(field + ".HalfDayRate", row.HalfDayRate);
                ValidateMoney(field + ".WholeDayRate", row.WholeDayRate);
                if (!seen.Add((row.Day, row.Date)))
                {
                    throw new PricingValidationException(field + (row.Date.HasValue ? ".Date" : ".Day"), "This day already has a rental price override.");
                }
            }
            return;
        }

        for (var index = 0; index < pricing.Bands.Length; index++)
        {
            var row = pricing.Bands[index];
            var field = $"Bands[{index}]";
            ValidateDay(field, row.Day, row.Date);
            if (row.StartMinute is < 0 or >= 1440)
            {
                throw new PricingValidationException(field + ".Start", "Enter a start time before midnight.");
            }
            if (row.EndMinute <= row.StartMinute || row.EndMinute > 1440)
            {
                throw new PricingValidationException(field + ".End", "Choose an end time after the start.");
            }
            ValidateMoney(field + ".Rate", row.Rate);
            if (pricing.Bands.Take(index).Any(other => other.Day == row.Day && other.Date == row.Date
                && other.StartMinute < row.EndMinute && row.StartMinute < other.EndMinute))
            {
                throw new PricingValidationException(field + ".Start", "This rate band overlaps another band for the same day.");
            }
        }
    }

    public static BookingPriceQuote Calculate(string service, ServicePricing pricing, DateOnly date, TimeOnly start,
        int durationMinutes, int bayCount, long revision = 0, string depositType = "None", decimal depositValue = 0)
    {
        List<PricingLine> lines = [];
        if (service == "WholeShop")
        {
            var rate = pricing.RentalOverrides.FirstOrDefault(row => row.Date == date)
                ?? pricing.RentalOverrides.FirstOrDefault(row => row.Day == date.DayOfWeek);
            var halfDay = durationMinutes == 360;
            lines.Add(halfDay ? new("Half-day rental", rate?.HalfDayRate ?? pricing.BaseRate)
                : new("Whole-day rental", rate?.WholeDayRate ?? pricing.WholeDayRate));
        }
        else if (IsPackage(service))
        {
            lines.Add(new($"Lesson package · {ClassCount(pricing.ClassCount)}", pricing.BaseRate));
        }
        else
        {
            if (bayCount is < 1 or > 80)
            {
                throw new ReservationValidationException("Choose between 1 and 80 bays for the quote.");
            }
            var firstMinute = start.Hour * 60 + start.Minute;
            var lastMinute = firstMinute + durationMinutes;
            var bands = pricing.Bands.Where(row => row.Date == date || row.Day == date.DayOfWeek).ToArray();
            var boundaries = bands.SelectMany(row => new[] { row.StartMinute, row.EndMinute })
                .Where(minute => minute > firstMinute && minute < lastMinute)
                .Append(firstMinute).Append(lastMinute).Distinct().Order().ToArray();
            for (var index = 0; index < boundaries.Length - 1; index++)
            {
                var from = boundaries[index];
                var to = boundaries[index + 1];
                var rate = bands.Where(row => row.StartMinute <= from && from < row.EndMinute)
                    .OrderByDescending(row => row.Date.HasValue).FirstOrDefault()?.Rate ?? pricing.BaseRate;
                var amount = rate * bayCount * (to - from) / 60m;
                var bays = bayCount == 1 ? "1 bay" : $"{bayCount} bays";
                lines.Add(new($"{Clock(from)}–{Clock(to)} · {Peso(rate)} per bay/hour × {bays}", amount));
            }
        }

        var rawTotal = lines.Sum(line => line.Amount);
        var total = Round(rawTotal);
        var deposit = service == "WholeShop" ? rawTotal / 2 : depositType switch
        {
            "Peso" => Math.Min(rawTotal, depositValue),
            "Percentage" => rawTotal * depositValue / 100m,
            _ => 0m
        };
        return new(total, Round(deposit), revision, lines);
    }

    public static string Summary(string service, ServicePricing pricing) => service switch
    {
        "WholeShop" => $"{Range(pricing.RentalOverrides.Select(row => row.HalfDayRate).Append(pricing.BaseRate))} / half day · "
            + $"{Range(pricing.RentalOverrides.Select(row => row.WholeDayRate).Append(pricing.WholeDayRate))} / whole day",
        _ when IsPackage(service) => $"{Peso(pricing.BaseRate)} / {ClassCount(pricing.ClassCount)}",
        _ => $"{Range(pricing.Bands.Select(row => row.Rate).Append(pricing.BaseRate))} / bay/hour"
    };

    private static void ValidateMoney(string field, decimal value)
    {
        if (value is <= 0 or > 1000000 || decimal.Round(value, 2) != value)
        {
            throw new PricingValidationException(field, "Enter a PHP rate above zero, up to ₱1,000,000, with at most two decimal places.");
        }
    }

    private static void ValidateDay(string field, DayOfWeek? day, DateOnly? date)
    {
        if (day.HasValue == date.HasValue || (day is { } weekday && !Enum.IsDefined(weekday)))
        {
            throw new PricingValidationException(field + ".Day", "Choose a weekday or enter a specific date.");
        }
    }

    private static string Clock(int minute) => $"{minute / 60:00}:{minute % 60:00}";
    private static string ClassCount(int count) => count == 1 ? "1 class" : $"{count} classes";
    private static string Peso(decimal amount) => $"₱{amount.ToString("#,##0.##", CultureInfo.InvariantCulture)}";
    private static decimal Round(decimal amount) => decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
    private static string Range(IEnumerable<decimal> amounts)
    {
        var values = amounts.ToArray();
        var min = values.Min();
        var max = values.Max();
        return min == max ? Peso(min) : $"{Peso(min)}–{Peso(max)}";
    }
}
