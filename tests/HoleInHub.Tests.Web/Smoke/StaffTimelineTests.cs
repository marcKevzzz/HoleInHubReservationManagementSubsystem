using HoleInHub.Business.Reservations;
using HoleInHubReservationManagementSubsystem.ViewModels;
using Xunit;

namespace HoleInHub.Tests.Web.Smoke;

public sealed class StaffTimelineTests
{
    [Fact]
    public void ExpiredFilterIncludesDepositExpirationsAndSortsMostRecentFirst()
    {
        var now = new DateTime(2026, 11, 8, 4, 0, 0, DateTimeKind.Utc);
        var earlier = new ReservationDto("earlier", "ref", "Regular", "Expired", now.AddDays(1), now.AddDays(1).AddHours(1),
            now.AddHours(-2), ["bay-2"], "Alex", "Santos", "09175550123", 1, []);
        var later = earlier with { Id = "later", PaymentStatus = "LatePayment", ReviewDeadlineUtc = now.AddHours(-1) };
        var pending = earlier with { Id = "pending", Status = "Pending" };
        var queue = new StaffReviewQueue([earlier, later, pending], "expired", null, "deadline", now);
        Assert.Equal(new[] { "later", "earlier" }, queue.Visible.Select(item => item.Id));
        Assert.False(queue.Overdue(earlier));
    }

    [Fact]
    public void BuyoutAppearsOnceButReservesEveryAllocatedBayInTheSummary()
    {
        var day = new DateOnly(2026, 11, 7);
        var start = day.ToDateTime(new TimeOnly(15, 0));
        var buyout = new StaffScheduleEntry(start, start.AddHours(6), "Confirmed", "buyout", "HIH-BUYOUT", "WholeShop", "Venue Guest", 12);
        var pending = new StaffScheduleEntry(start.AddHours(6), start.AddHours(7), "Pending", "regular", "HIH-REGULAR");
        var timeline = new StaffTimeline(day, [new("Bay 1", true, [buyout, pending]), new("VIP 1", true, [buyout], "VIP")]);
        var rows = timeline.DisplayBays();

        Assert.Equal("Venue buyout", rows[0].Bay.Name);
        Assert.Single(rows.SelectMany(row => row.Bay.Entries), entry => entry.ReservationId == "buyout");
        Assert.Equal(2, timeline.Bookings.Count);
        Assert.Equal((2, 2, start), timeline.Peak());
        Assert.Empty(rows[2].Bay.Entries);
        Assert.Single(rows[2].Coverage);
    }

    [Fact]
    public void ClipsAtMidnightAndKeepsOverlappingBlocksInSeparateLanes()
    {
        var day = new DateOnly(2026, 11, 9);
        var evening = day.ToDateTime(new TimeOnly(22, 0));
        var bay = new StaffScheduleBay("Bay 1", true,
        [
            new(evening, evening.AddHours(1), "Confirmed"),
            new(evening.AddMinutes(30), evening.AddHours(3), "Blocked"),
            new(evening.AddHours(1), evening.AddHours(2), "Pending")
        ]);
        var timeline = new StaffTimeline(day, [bay]);
        Assert.Equal(11, timeline.FirstHour);
        Assert.Equal(13, timeline.Hours);
        var bars = timeline.Bars(bay);
        Assert.Equal(new[] { 0, 1, 0 }, bars.Select(bar => bar.Lane));
        Assert.Equal(100, bars[1].StartPercent + bars[1].WidthPercent, 6);
        Assert.Equal(bars[2].StartPercent, bars[0].StartPercent + bars[0].WidthPercent, 6);
    }

    [Fact]
    public void EarlierBlocksExtendTheAxisAndKeepTheVisiblePartOfAnOvernightBlock()
    {
        var day = new DateOnly(2026, 11, 10);
        var midnight = day.ToDateTime(TimeOnly.MinValue);
        var bay = new StaffScheduleBay("Bay 1", true, [new(midnight.AddHours(-2), midnight.AddHours(6), "Blocked")]);
        var timeline = new StaffTimeline(day, [bay]);
        var bar = Assert.Single(timeline.Bars(bay));
        Assert.Equal(0, timeline.FirstHour);
        Assert.Equal(0, bar.StartPercent);
        Assert.Equal(25, bar.WidthPercent);
    }
    [Fact]
    public void CompletedBuyoutRemainsVisibleWithoutCapacityOrCoverage()
    {
        var day = new DateOnly(2026, 11, 10); var start = day.ToDateTime(new TimeOnly(13, 0));
        var completed = new StaffScheduleEntry(start, start.AddHours(2), "Completed", "done", "HIH-DONE", "WholeShop");
        var timeline = new StaffTimeline(day, [new("Bay 1", true, [completed]), new("VIP 1", true, [completed], "VIP")]);
        Assert.False(completed.HoldsCapacity);
        Assert.Single(timeline.Bookings);
        Assert.Equal(0, timeline.Peak().Reserved);
        Assert.All(timeline.DisplayBays().Skip(1), row => Assert.Empty(row.Coverage));
    }

}
