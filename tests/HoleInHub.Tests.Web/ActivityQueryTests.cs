using HoleInHubReservationManagementSubsystem.ViewModels;
using Microsoft.AspNetCore.WebUtilities;
using Xunit;

namespace HoleInHub.Tests.Web;

public sealed class ActivityQueryTests
{
    [Fact]
    public void NextActivityPageAdvancesAndFilterResetReturnsToFirstPage()
    {
        var query = new ActivityQuery { Cursor = "current", Page = 3 };
        Assert.Equal("4", QueryHelpers.ParseQuery(new Uri("https://example.test" + query.Url(cursor: "next")).Query)["page"]);
        var reset = QueryHelpers.ParseQuery(new Uri("https://example.test" + query.Url(reset: true)).Query);
        Assert.False(reset.ContainsKey("page"));
        Assert.False(reset.ContainsKey("cursor"));
    }
}
