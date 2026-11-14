using HoleInHubReservationManagementSubsystem.ViewModels;
using Xunit;

namespace HoleInHub.Tests.Web;

public sealed class DashboardTrendTests
{
    [Theory]
    [InlineData(15, 10, true, "positive", "50%")]
    [InlineData(5, 10, true, "negative", "50%")]
    [InlineData(5, 10, false, "positive", "50%")]
    [InlineData(15, 10, false, "negative", "50%")]
    [InlineData(5, 0, true, "positive", "New")]
    [InlineData(0, 0, true, "neutral", "No change")]
    public void ChangesHandleZeroBaselinesAndWhetherAnIncreaseIsHelpful(double current, double previous, bool increaseIsGood, string tone, string change)
    {
        var trend = new DashboardTrend(current, previous, "previous day", increaseIsGood);
        Assert.Equal(tone, trend.Tone);
        Assert.Equal(change, trend.Change);
    }
}
