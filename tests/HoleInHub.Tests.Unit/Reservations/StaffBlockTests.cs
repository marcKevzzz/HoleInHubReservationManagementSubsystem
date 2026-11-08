using HoleInHub.Business.Reservations;
using Microsoft.Extensions.Options;
using Xunit;

namespace HoleInHub.Tests.Unit.Reservations;

public sealed class StaffBlockTests
{
    private static readonly DateOnly Day = new(2026, 11, 10);
    private sealed class Clock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 11, 10, 4, 15, 0, TimeSpan.Zero);
    }
    private static ReservationService Service() => new(null!, null!, Options.Create(new ReservationPolicy()), new Clock());
    private static StaffBlockRequest Request() => new("bay-1", Day, new TimeOnly(13, 0), 60, "Maintenance", "key");

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public async Task ReasonIsRequired(string? reason) =>
        await Assert.ThrowsAsync<ReservationValidationException>(() => Service().CreateScheduleBlockAsync(Request() with { Reason = reason! }, "admin", default));

    [Fact]
    public async Task LongReasonAndMissingIdentityAreRejected()
    {
        await Assert.ThrowsAsync<ReservationValidationException>(() => Service().CreateScheduleBlockAsync(Request() with { Reason = new string('x', 501) }, "admin", default));
        await Assert.ThrowsAsync<ReservationValidationException>(() => Service().CreateScheduleBlockAsync(Request(), "", default));
        await Assert.ThrowsAsync<ReservationValidationException>(() => Service().CreateScheduleBlockAsync(Request() with { IdempotencyKey = "" }, "admin", default));
        await Assert.ThrowsAsync<ReservationValidationException>(() => Service().CreateScheduleBlockAsync(Request() with { ResourceId = "" }, "admin", default));
    }

    [Theory]
    [InlineData(9, 0, 60)]
    [InlineData(13, 15, 60)]
    [InlineData(13, 0, 45)]
    [InlineData(23, 30, 60)]
    public async Task InvalidOrPastIntervalsAreRejected(int hour, int minute, int duration) =>
        await Assert.ThrowsAsync<ReservationValidationException>(() => Service().CreateScheduleBlockAsync(
            Request() with { StartTime = new TimeOnly(hour, minute), DurationMinutes = duration }, "admin", default));
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public async Task ReleaseReasonIsRequired(string? reason) =>
        await Assert.ThrowsAsync<ReservationValidationException>(() => Service().ReleaseScheduleBlockAsync("block", reason!, "admin", default));

    [Fact]
    public async Task ReleaseRejectsMissingIdentityAndLongReason()
    {
        await Assert.ThrowsAsync<ReservationValidationException>(() => Service().ReleaseScheduleBlockAsync("", "Finished", "admin", default));
        await Assert.ThrowsAsync<ReservationValidationException>(() => Service().ReleaseScheduleBlockAsync("block", "Finished", "", default));
        await Assert.ThrowsAsync<ReservationValidationException>(() => Service().ReleaseScheduleBlockAsync("block", new string('x', 501), "admin", default));
    }

}
