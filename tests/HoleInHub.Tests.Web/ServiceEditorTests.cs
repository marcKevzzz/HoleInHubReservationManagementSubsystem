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
    public void NewRatesSupportBothUnitsAndRejectPercentAbove100()
    {
        var form = new ServiceEditorForm { RateAmount = 25, RateUnit = "Percentage", Summary = "Summary", ImageOrder = ["upload:1", "upload:0"] };
        Assert.Equal("25%", form.Command().RateText);
        Assert.Equal("Summary", form.Command().Details);
        Assert.Equal(form.ImageOrder, form.Command().ImageOrder);
        form.RateAmount = 101;
        Assert.Throws<ArgumentException>(() => form.Command());
        form.RateUnit = "Peso";
        Assert.Contains("101", form.Command().RateText);
    }
}
