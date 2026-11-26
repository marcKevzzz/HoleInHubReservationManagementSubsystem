using HoleInHub.Business.Reservations;
using HoleInHubReservationManagementSubsystem.ViewModels;
using Xunit;

namespace HoleInHub.Tests.Web.Smoke;

public sealed class CoachMidnightTests
{
    [Fact]
    public void MidnightWindowsRoundTripWithoutBecomingInvalid()
    {
        var coach = new CoachDto("coach", "Coach", true, 0,
            [new(DayOfWeek.Monday, 660, 1440)], [new("2026-12-01", 600, 1440)]);
        var form = CoachEditorForm.From(coach);
        Assert.Empty(form.ValidateSchedule());
        Assert.Equal(1440, CoachEditorForm.EndMinute(form.Weekly.Single(x => x.Day == DayOfWeek.Monday).End!.Value));
        Assert.Equal(1440, CoachEditorForm.EndMinute(form.Exceptions[0].End!.Value));
    }

    [Fact]
    public void MidnightWindowStillDetectsOverlappingShift()
    {
        var form = new CoachEditorForm
        {
            Weekly = [
            new() { Day = DayOfWeek.Monday, Start = new(18, 0), End = new(0, 0) },
            new() { Day = DayOfWeek.Monday, Start = new(22, 0), End = new(23, 0) }]
        };
        Assert.Contains(form.ValidateSchedule(), error => error.ErrorMessage!.Contains("overlaps"));
    }

    [Fact]
    public void EarlierNonMidnightEndRemainsInvalid()
    {
        var form = new CoachEditorForm
        {
            Weekly = [
            new() { Day = DayOfWeek.Monday, Start = new(18, 0), End = new(17, 0) }]
        };
        Assert.NotEmpty(form.ValidateSchedule());
    }
}
