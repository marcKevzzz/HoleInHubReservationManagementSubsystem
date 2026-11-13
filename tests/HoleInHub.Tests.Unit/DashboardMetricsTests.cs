using HoleInHub.Business.Reservations;
using HoleInHub.Data.Reservations;
using Xunit;

namespace HoleInHub.Tests.Unit;

public sealed class DashboardMetricsTests
{
    private static readonly DateOnly Day = new(2026, 11, 13);
    private static readonly DateTime Start = DashboardMetrics.Utc(Day);
    private static DashboardRow Row(string id, string status = "Confirmed", int hour = 11, int duration = 120, string[]? bays = null) => new()
    {
        Id = id, Status = status, Service = "Regular", ResourceIds = bays ?? ["bay-2"], StartUtc = Start.AddHours(hour), EndUtc = Start.AddHours(hour).AddMinutes(duration), Guest = "Guest"
    };
    private static DashboardSnapshot Calculate(params DashboardRow[] rows) => DashboardMetrics.Calculate(Day, Day, Start.AddHours(10), rows,
        new Dictionary<string, long>(), new Dictionary<string, string> { ["bay-2"] = "Regular", ["vip-7"] = "VIP" }, []);

    [Fact]
    public void MultibayAndWholeShopCountOnceButEveryDistinctBayContributesHours()
    {
        var multi = Row("multi", bays: ["bay-2", "vip-7", "bay-2"]);
        var whole = Row("whole", hour: 14, duration: 60, bays: ["bay-2", "vip-7"]); whole.Service = "WholeShop";
        var data = Calculate(multi, multi, whole);
        Assert.Equal(2, data.Scheduled);
        Assert.Equal(6, data.Services.Sum(x => x.Hours));
        Assert.Equal(6, data.Hours.Sum(x => x.Regular + x.Vip + x.Other));
        Assert.Equal(2, data.Days.Single().Booked);
    }

    [Fact]
    public void PendingAndTerminalOutcomesDoNotInflateBookedHoursOrServiceUsage()
    {
        var data = Calculate(Row("pending", "Pending"), Row("cancel", "Cancelled"), Row("noshow", "No-show"), Row("legacy", "LegacyOutcome"), Row("complete", "Completed"));
        Assert.Equal(2, data.Scheduled);
        Assert.Equal(1, data.Completed);
        Assert.Equal(1, data.Days.Single().Pending);
        Assert.Equal(3, data.Days.Single().Other);
        Assert.Equal(1, data.Days.Single().Statuses["LegacyOutcome"]);
        Assert.Equal(2, data.Services.Single().Hours);
    }

    [Fact]
    public void MidnightOverlapIsClippedAndDailyTrendUsesManilaStartDate()
    {
        var overnight = Row("overnight", hour: -1, duration: 120);
        var data = Calculate(overnight, Row("next", hour: 24));
        Assert.Equal(1, data.Scheduled);
        Assert.Equal(1, data.Services.Single().Hours);
        Assert.Equal(0, data.Days.Single().Booked);
        Assert.Equal(1, data.Hours.Single(x => x.Hour == 0).Regular);
        Assert.Equal(new DateTime(2026, 11, 12, 16, 0, 0, DateTimeKind.Utc), Start);
    }

    [Fact]
    public void ArrivalsExcludeCancellationRequestsAndRecordedCheckIns()
    {
        var cancellation = Row("cancel"); cancellation.CancellationRequested = true;
        var checkedIn = Row("checked"); checkedIn.CheckedInUtc = Start.AddHours(11);
        var due = Row("due", hour: 9, duration: 120);
        var data = Calculate(cancellation, checkedIn, due);
        Assert.Equal(1, data.ArrivalsDue);
        Assert.True(data.Arrivals.Single().FollowUp);
    }

    [Fact]
    public void UnknownResourceTypeRemainsVisibleAndEmptyBucketsAreZero()
    {
        var data = Calculate(Row("unknown", bays: ["retired-unknown"]));
        Assert.Equal(2, data.Hours.Sum(x => x.Other));
        Assert.Equal(24, data.Hours.Count);
        var empty = Calculate();
        Assert.Empty(empty.Services); Assert.Empty(empty.Arrivals); Assert.Equal(0, empty.Scheduled);
        Assert.All(empty.Hours, hour => Assert.Equal(0, hour.Regular + hour.Vip + hour.Other));
    }

    [Fact]
    public void RangesAreBoundedAndReversedDatesAreRejected()
    {
        DashboardMetrics.ValidateRange(Day, Day.AddDays(89));
        Assert.Throws<ReservationValidationException>(() => DashboardMetrics.ValidateRange(Day, Day.AddDays(90)));
        Assert.Throws<ReservationValidationException>(() => DashboardMetrics.ValidateRange(Day, Day.AddDays(-1)));
    }

    [Fact]
    public void ComparisonUsesAdjacentEqualCalendarPeriodsAndClippedDistinctBayHours()
    {
        var range = DashboardMetrics.PreviousRange(Day.AddDays(-6), Day)!.Value;
        Assert.Equal(Day.AddDays(-13), range.From);
        Assert.Equal(Day.AddDays(-7), range.To);
        Assert.Null(DashboardMetrics.PreviousRange(new(2000, 1, 1), new(2000, 1, 1)));
        var overnight = Row("overnight", "Completed", -1, 120, ["bay-2", "bay-2"]);
        var pending = Row("pending", "Pending");
        var data = DashboardMetrics.Baseline(Day, Day, [overnight, overnight, pending, Row("cancelled", "Cancelled"), Row("next", hour: 24)]);
        Assert.Equal(2, data.Scheduled);
        Assert.Equal(1, data.Booked);
        Assert.Equal(1, data.Pending);
        Assert.Equal(1, data.BayHours);
        Assert.Equal(1, data.Completed);
    }
}
