using HoleInHub.Business.Reservations;
using HoleInHub.Data.Reservations;
using Xunit;

namespace HoleInHub.Tests.Unit;

public sealed class ReservationReportTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void VolumeAndBayHoursUseClippedManilaIntervalsAndCustomerCountsUseDistinctProfiles(int extraDays)
    {
        var day = new DateOnly(2026, 11, 20);
        var start = DashboardMetrics.Utc(day);
        var end = DashboardMetrics.Utc(day.AddDays(extraDays + 1));
        DashboardRow Row(string id, DateTime begins, DateTime ends, string status) => new()
        {
            Id = id,
            StartUtc = begins,
            EndUtc = ends,
            Service = "WholeShop",
            Status = status,
            ResourceIds = ["r1", "r2", "r1"],
            ResourceNames = new() { ["r1"] = "East" }
        };
        var rows = new[] { Row("1", start.AddHours(-1), start.AddMinutes(90), "Confirmed"),
            Row("2", start, start.AddHours(12), "Pending"), Row("3", end.AddMinutes(-30), end.AddHours(1), "Completed"),
            Row("4", start.AddHours(2), start.AddHours(3), "Cancelled") };
        var report = ReservationService.CalculateReport(day, day.AddDays(extraDays), rows,
            new Dictionary<string, string> { ["WholeShop"] = "Whole shop" },
            new Dictionary<string, string> { ["1"] = "a", ["2"] = "a", ["4"] = "b" }, new HashSet<string> { "a" });
        Assert.Equal(4, report.Volume.Values.Sum());
        Assert.Equal(extraDays == 0 ? 2 : 3, report.Volume[extraDays == 0 ? "00:00" : "2026-11-20"]);
        Assert.Equal(0, report.Volume[extraDays == 0 ? "05:00" : "2026-11-21"]);
        Assert.Equal(4, report.ServiceCounts["Whole shop"]);
        Assert.Equal(4, report.BayHours.Values.Sum());
        Assert.Equal(2, report.BayHours["00:00"]);
        Assert.Equal(1, report.BayHours["01:00"]);
        Assert.Equal(1, report.BayHours["23:00"]);
        Assert.Equal((1, 1, 1), (report.NewCustomers, report.ReturningCustomers, report.UnmatchedReservations));
        Assert.Equal(new[] { "East", "r2", "East" }, report.Rows[0].Bays);
    }
}
