using HoleInHub.Business.Reservations;
using Xunit;
using HoleInHub.Data.Services;

namespace HoleInHub.Tests.Unit.Reservations;

public sealed class PricingRulesTests
{
    [Fact]
    public void RegularQuoteSplitsWeekdayRateAtThreeAndPricesBothBays()
    {
        var quote = PricingRules.Calculate("Regular", PricingRules.Default("Regular"),
            new DateOnly(2026, 11, 16), new TimeOnly(14, 30), 120, 2);

        Assert.Equal(3000m, quote.Total);
        Assert.Equal([600m, 2400m], quote.Lines.Select(line => line.Amount));
    }

    [Fact]
    public void DatedRateOverridesOnlyTheCoveredPortionOfAWeeklyBand()
    {
        var date = new DateOnly(2026, 12, 25);
        var pricing = PricingRules.Default("Regular");
        pricing = pricing with { Bands = [..pricing.Bands, new(null, date, 900, 930, 1100m)] };

        var quote = PricingRules.Calculate("Regular", pricing, date, new TimeOnly(14, 30), 90, 1);

        Assert.Equal(1250m, quote.Total);
    }

    [Fact]
    public void FourClassPackageDoesNotMultiplyItsPriceByTheNumberOfBays()
    {
        var pricing = PricingRules.Default("KpgaGroup");
        var quote = PricingRules.Calculate("KpgaGroup", pricing, new DateOnly(2026, 11, 17), new TimeOnly(15, 0), 60, 2);

        Assert.Equal(2500m, quote.Total);
        Assert.Equal("₱2,500 / 4 classes", PricingRules.Summary("KpgaGroup", pricing));
    }

    [Theory]
    [InlineData(16, 360, 40000)]
    [InlineData(16, 780, 60000)]
    [InlineData(20, 360, 50000)]
    [InlineData(20, 840, 80000)]
    public void WholeShopQuotePreservesPublishedPricesAndHalfDeposit(int day, int minutes, int total)
    {
        var quote = ReservationService.PriceBooking(null, "WholeShop", new DateOnly(2026, 11, day),
            new TimeOnly(minutes == 360 ? 15 : day == 16 ? 11 : 10, 0), minutes, 0);

        Assert.Equal(total, quote.Total);
        Assert.Equal(total / 2m, quote.DepositAmount);
    }

    [Fact]
    public void OverlappingWeeklyBandsIdentifyTheConflictingRow()
    {
        var pricing = new ServicePricing
        {
            BaseRate = 600m,
            Bands = [new(DayOfWeek.Monday, null, 600, 720, 800m), new(DayOfWeek.Monday, null, 690, 750, 900m)]
        };

        var error = Assert.Throws<PricingValidationException>(() => PricingRules.Validate("Regular", pricing));

        Assert.Equal("Bands[1].Start", error.Field);
    }

    [Fact]
    public void FractionalBandChargesAreRoundedAfterSumming()
    {
        var pricing = new ServicePricing
        {
            BaseRate = 10m,
            Bands = [new(DayOfWeek.Monday, null, 661, 662, 10.01m)]
        };

        var quote = PricingRules.Calculate("Regular", pricing, new DateOnly(2026, 11, 16), new TimeOnly(11, 0), 30, 1);

        Assert.Equal(5m, quote.Total);
    }
}
