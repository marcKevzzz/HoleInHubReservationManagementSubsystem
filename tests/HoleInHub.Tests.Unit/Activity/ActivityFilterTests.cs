using HoleInHub.Business.Activity;
using Xunit;

namespace HoleInHub.Tests.Unit.Activity;

public sealed class ActivityFilterTests
{
    private static readonly DateTime Now = new(2026, 11, 11, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ManilaCalendarRangeIsInclusiveAndMyActivityUsesStableId()
    {
        var query = ActivityService.Validate(new(new(2026, 11, 11), new(2026, 11, 11), Actor: "mine"), false, "employee-id", Now);
        Assert.Equal(new DateTime(2026, 11, 10, 16, 0, 0, DateTimeKind.Utc), query.FromUtc);
        Assert.Equal(new DateTime(2026, 11, 11, 16, 0, 0, DateTimeKind.Utc), query.ToUtc);
        Assert.Equal("employee-id", query.ActorId);
        Assert.Equal(Now, query.AsOfUtc);
    }

    [Fact]
    public void AcceptsExactlyNinetyDaysAndRejectsLongerOrReversedRanges()
    {
        var first = new DateOnly(2026, 1, 1);
        ActivityService.Validate(new(first, first.AddDays(89)), false, "staff", Now);
        Assert.Throws<ArgumentException>(() => ActivityService.Validate(new(first, first.AddDays(90)), false, "staff", Now));
        Assert.Throws<ArgumentException>(() => ActivityService.Validate(new(first, first.AddDays(-1)), false, "staff", Now));
    }

    [Fact]
    public void AccessScopeCannotBeForgedThroughCategoryOrCursor()
    {
        var day = new DateOnly(2026, 11, 11);
        Assert.Throws<UnauthorizedAccessException>(() => ActivityService.Validate(new(day, day, Category: "Access"), false, "staff", Now));
        Assert.Equal("Access", ActivityService.Validate(new(day, day, Category: "Access"), true, "admin", Now).Category);
        Assert.Throws<ArgumentException>(() => ActivityService.Validate(new(day, day, Cursor: "invalid!"), true, "admin", Now));
        Assert.Throws<ArgumentException>(() => ActivityService.Validate(new(day, day, Search: new string('x', 121)), true, "admin", Now));
    }
}
