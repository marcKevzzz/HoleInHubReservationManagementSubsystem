using HoleInHubReservationManagementSubsystem.ViewModels;
using Xunit;
namespace HoleInHub.Tests.Web;

public sealed class ServiceEditorTests
{
    [Fact]
    public void LegacyRateAndDescriptionSurviveUnchangedEdits()
    {
        var form = new ServiceEditorForm { Create = false, RateText = "₱600–₱800 / hour", RateAmount = 600, Details = "Existing inclusions", Summary = "Summary", MaxPax = 8 };
        var command = form.Command();
        Assert.Equal("₱600–₱800 / hour", command.RateText);
        Assert.Equal("Existing inclusions", command.Details);
        Assert.Equal(8, command.MaxPax);
    }
    [Fact]
    public void RatesUsePhpAndNewBookingTypesRequireAName()
    {
        var form = new ServiceEditorForm { RateAmount = 25, RateUnit = "Percentage", Summary = "Summary", ImageOrder = ["upload:1", "upload:0"] };
        Assert.Throws<ArgumentException>(() => form.Command());
        form.RateUnit = "Peso";
        Assert.Contains("25", form.Command().RateText);
        Assert.Equal("Summary", form.Command().Details);
        Assert.Equal(form.ImageOrder, form.Command().ImageOrder);
        form.RateAmount = 1000001;
        Assert.Throws<ArgumentException>(() => form.Command());
        form.RateAmount = 101;
        Assert.Contains("101", form.Command().RateText);
        form.BookingTypeName = "__new";
        Assert.Throws<ArgumentException>(() => form.Command());
        form.NewBookingTypeName = " Weekend promo ";
        var command = form.Command();
        Assert.Equal("Weekend promo", command.BookingTypeName);
        Assert.Equal("Book", command.Action);
    }
}
