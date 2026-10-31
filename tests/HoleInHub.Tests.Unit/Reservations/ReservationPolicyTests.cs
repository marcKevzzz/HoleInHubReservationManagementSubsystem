using HoleInHub.Business.Reservations;
using Xunit;
namespace HoleInHub.Tests.Unit.Reservations;

public class ReservationPolicyTests
{
    [Fact] public void MissingApprovalRejectsSubmission() => Assert.Throws<ReservationValidationException>(() => new ReservationPolicy().ValidateCoverage(DateTimeOffset.UtcNow));
    [Fact]
    public void EntireReviewWindowMustFitCoverage()
    {
        var policy = new ReservationPolicy { Approved = true, DurationsApproved = true, ReviewStart = TimeSpan.FromHours(10), ReviewEnd = TimeSpan.FromHours(22), ReviewDays = [2], StaffEmails = ["staff@example.test"] };
        policy.ValidateCoverage(new DateTimeOffset(2026, 11, 3, 21, 30, 0, TimeSpan.FromHours(8)));
        Assert.Throws<ReservationValidationException>(() => policy.ValidateCoverage(new DateTimeOffset(2026, 11, 3, 21, 31, 0, TimeSpan.FromHours(8))));
    }
    [Fact] public void InvalidMaximumCalendarDateIsRejected() => Assert.Throws<ReservationValidationException>(() => ReservationRules.Interval(DateOnly.MaxValue, new TimeOnly(23, 30), 30));
    [Fact] public void FractionalStartIsRejected() => Assert.Throws<ReservationValidationException>(() => ReservationRules.Interval(new DateOnly(2026, 11, 3), new TimeOnly(10, 0, 0, 1), 30));
    [Fact] public void MondayCannotStartBeforeEleven() => Assert.Throws<ReservationValidationException>(() => ReservationRules.Interval(new DateOnly(2026, 11, 2), new TimeOnly(10, 30), 30));
    [Fact] public void MidnightEndIsFollowingDay() { var (start, end) = ReservationRules.Interval(new DateOnly(2026, 11, 3), new TimeOnly(23, 30), 30); Assert.Equal(30, (end - start).TotalMinutes); Assert.Equal(16, end.Hour); }
    [Theory]
    [InlineData(2, 11, 780)]
    [InlineData(3, 10, 840)]
    public void WholeShopPreviewRunsFromOpeningToMidnight(int day, int opening, int minutes)
    {
        var (start, end) = ReservationRules.PreviewInterval(new DateOnly(2026, 11, day), new TimeOnly(opening, 0), minutes, "WholeShop");
        Assert.Equal(minutes, (end - start).TotalMinutes);
        Assert.Equal(16, end.Hour);
    }
    [Fact]
    public void ProgramPreviewEnforcesPrototypeSchedules()
    {
        ReservationRules.PreviewInterval(new DateOnly(2026, 11, 3), new TimeOnly(15, 0), 60, "KpgaGroup");
        Assert.Throws<ReservationValidationException>(() => ReservationRules.PreviewInterval(new DateOnly(2026, 11, 4), new TimeOnly(15, 0), 60, "KpgaGroup"));
        Assert.Throws<ReservationValidationException>(() => ReservationRules.PreviewInterval(new DateOnly(2026, 11, 3), new TimeOnly(14, 0), 60, "KpgaGroup"));
        Assert.Throws<ReservationValidationException>(() => ReservationRules.PreviewInterval(new DateOnly(2026, 11, 4), new TimeOnly(15, 0), 60, "KpgaPro"));
        Assert.Throws<ReservationValidationException>(() => ReservationRules.PreviewInterval(new DateOnly(2026, 11, 3), new TimeOnly(14, 0), 360, "WholeShop"));
    }
    [Theory]
    [InlineData("PhilippineCoach")]
    [InlineData("KpgaPro")]
    [InlineData("KpgaGroup")]
    [InlineData("WholeShop")]
    public async Task PreviewServicesCannotCreateUnapprovedHolds(string service)
    {
        var business = new ReservationService(null!, null!, Microsoft.Extensions.Options.Options.Create(new ReservationPolicy()));
        await Assert.ThrowsAsync<ReservationValidationException>(() => business.SubmitAsync(new SubmitReservationRequest { Service = service }, "customer", "customer@example.test", CancellationToken.None));
    }
}

