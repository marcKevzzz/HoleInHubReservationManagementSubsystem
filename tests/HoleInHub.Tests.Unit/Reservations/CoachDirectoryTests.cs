using HoleInHub.Business.Reservations;
using HoleInHub.Data.Reservations;
using Xunit;

namespace HoleInHub.Tests.Unit.Reservations;

public sealed class CoachDirectoryTests
{
    // Manila Monday starts on Sunday in UTC.
    private static readonly DateTime Midnight = new(2026, 11, 22, 16, 0, 0, DateTimeKind.Utc);
    private static DateTime At(int minutes) => Midnight.AddMinutes(minutes);
    private static CoachRecord Coach(params CoachWeeklyHours[] hours) => new() { Id = "maria", Active = true, WeeklyHours = hours };
    private static CoachAssignmentInterval Hold(int start, int end, string id = "maria") => new(id, At(start), At(end));

    [Fact]
    public void FreeNowClipsAtCurrentInstantAndMergesTouchingShiftsAcrossMidnight()
    {
        var coach = Coach(new(DayOfWeek.Monday, 1100, 1440), new(DayOfWeek.Tuesday, 0, 30));
        var now = At(1200).AddSeconds(35);
        Assert.Equal(new CoachFreePeriod(now, At(1470), true), ReservationService.NextCoachFreePeriod(coach, now,
            [Hold(900, 1200), Hold(1300, 1400, "another"), Hold(1470, 1480)]));
    }

    [Fact]
    public void OverlappingAndAdjacentAssignmentsSplitAnOverrideIntoFreePeriods()
    {
        var coach = Coach(new CoachWeeklyHours(DayOfWeek.Monday, 0, 10));
        coach.Overrides = [new("2026-11-23", 0, 60), new("2026-11-23", 60, 120)];
        CoachAssignmentInterval[] holds = [Hold(70, 110), Hold(25, 45), Hold(-5, 30), Hold(45, 50)];
        Assert.Equal(new CoachFreePeriod(At(50), At(70), false), ReservationService.NextCoachFreePeriod(coach, At(0), holds));
        Assert.Equal(new CoachFreePeriod(At(50), At(70), true), ReservationService.NextCoachFreePeriod(coach, At(50), holds));
    }

    [Fact]
    public void AHoldCanCrossAClosedGapAndTheNextShift()
    {
        var coach = Coach(new(DayOfWeek.Monday, 0, 20), new(DayOfWeek.Monday, 40, 60));
        Assert.Equal(new CoachFreePeriod(At(55), At(60), false), ReservationService.NextCoachFreePeriod(coach, At(0), [Hold(-1, 55)]));
    }

    [Fact]
    public void ClosedDateReplacesWeeklyHoursAndLastDateFallsInsideHorizon()
    {
        var coach = Coach(new CoachWeeklyHours(DayOfWeek.Monday, 10, 20));
        coach.Overrides = [new("2026-11-23", null, null), new("2026-12-22", 1430, 1440), new("2026-12-23", 0, 20)];
        var period = ReservationService.NextCoachFreePeriod(coach, At(0), [Hold(1440, 29 * 1440)]);
        Assert.Equal(new CoachFreePeriod(At(30 * 1440 - 10), At(30 * 1440), false), period);
    }

    [Fact]
    public void InactiveFullyOccupiedEmptyAndOutsideHorizonHaveNoFreePeriod()
    {
        var coach = Coach();
        Assert.Null(ReservationService.NextCoachFreePeriod(coach, At(0), []));
        coach.Overrides = [new("2026-12-23", 0, 300)];
        Assert.Null(ReservationService.NextCoachFreePeriod(coach, At(0), []));
        coach.WeeklyHours = [new(DayOfWeek.Monday, 0, 300)];
        Assert.Null(ReservationService.NextCoachFreePeriod(coach, At(0), [Hold(-60, 30 * 1440)]));
        coach.Active = false;
        Assert.Null(ReservationService.NextCoachFreePeriod(coach, At(0), []));
    }
}
