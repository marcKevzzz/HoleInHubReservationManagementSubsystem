using HoleInHub.Business.Reservations;
using Xunit;

namespace HoleInHub.Tests.Unit.Reservations;

public sealed class ReviewClockTests
{
    [Theory]
    [InlineData(3, 1, 0, 30, 3, 10, 30)]
    [InlineData(3, 1, 0, 60, 3, 11, 0)]
    [InlineData(2, 1, 0, 30, 2, 11, 30)]
    [InlineData(3, 23, 50, 30, 4, 10, 20)]
    [InlineData(1, 23, 50, 60, 2, 11, 50)]
    [InlineData(3, 23, 30, 30, 4, 0, 0)]
    public void OperatingMinutesPauseOutsideHours(int day, int hour, int minute, int duration, int dueDay, int dueHour, int dueMinute)
    {
        var now = new DateTimeOffset(2026, 11, day, hour, minute, 0, TimeSpan.FromHours(8));
        Assert.Equal(new DateTimeOffset(2026, 11, dueDay, dueHour, dueMinute, 0, TimeSpan.FromHours(8)).UtcDateTime,
            ReviewClock.AddMinutes(now, duration));
    }

    [Fact]
    public void NarrowCoverageSkipsUnstaffedDaysAndPreservesSeconds()
    {
        var policy = new ReservationPolicy { ReviewStart = TimeSpan.FromHours(10), ReviewEnd = TimeSpan.FromHours(22), ReviewDays = [2] };
        var now = new DateTimeOffset(2026, 11, 3, 21, 50, 30, TimeSpan.FromHours(8));
        Assert.Equal(new DateTimeOffset(2026, 11, 10, 10, 20, 30, TimeSpan.FromHours(8)).UtcDateTime,
            ReviewClock.AddMinutes(now, 30, policy));
    }

    [Fact]
    public void OvernightRequestNeedsReviewToFinishBeforeSession()
    {
        var now = new DateTimeOffset(2026, 11, 3, 1, 0, 0, TimeSpan.FromHours(8));
        Assert.Throws<ReservationValidationException>(() => BookingRules.ValidateWindow("Regular", now.Date.AddHours(2), now));
        BookingRules.ValidateWindow("Regular", new DateTimeOffset(2026, 11, 3, 10, 30, 0, TimeSpan.FromHours(8)).UtcDateTime, now);
        Assert.Equal(now.UtcDateTime.AddHours(1), now.UtcDateTime.AddMinutes(BookingRules.ReviewMinutes("WholeShop")));
    }
}
