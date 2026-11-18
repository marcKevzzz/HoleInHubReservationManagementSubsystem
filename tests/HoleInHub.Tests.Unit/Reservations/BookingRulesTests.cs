using HoleInHub.Business.Reservations;
using Xunit;

namespace HoleInHub.Tests.Unit.Reservations;

public sealed class BookingRulesTests
{
    [Fact]
    public void HalfHourSessionIsAccepted()
    {
        var (start, end) = ReservationRules.Interval(new DateOnly(2026, 11, 3), new TimeOnly(12, 0), 30);
        Assert.Equal(30, (end - start).TotalMinutes);
    }

    [Fact]
    public void SessionMayFillAllOpeningHours()
    {
        var (start, end) = ReservationRules.Interval(new DateOnly(2026, 11, 3), new TimeOnly(10, 0), 840);
        Assert.Equal(840, (end - start).TotalMinutes);
    }

    [Theory]
    [InlineData("Regular", 30)]
    [InlineData("VIP", 60)]
    [InlineData("PhilippineCoach", 60)]
    [InlineData("WholeShop", 60)]
    public void FullReviewWindowMustFitBeforeStart(string service, int minutes)
    {
        var now = new DateTimeOffset(2026, 11, 3, 12, 0, 0, TimeSpan.FromHours(8));
        BookingRules.ValidateWindow(service, now.UtcDateTime.AddMinutes(minutes), now);
        Assert.Throws<ReservationValidationException>(() => BookingRules.ValidateWindow(service, now.UtcDateTime.AddMinutes(minutes).AddSeconds(-1), now));
    }
    [Fact]
    public void ReviewCarriesAcrossClosingAndAcceptsOutsideHoursSubmission()
    {
        var now = new DateTimeOffset(2026, 11, 3, 23, 1, 0, TimeSpan.FromHours(8));
        BookingRules.ValidateWindow("VIP", now.UtcDateTime.AddDays(1), now);
        BookingRules.ValidateWindow("Regular", now.UtcDateTime.AddDays(1), now);
        BookingRules.ValidateWindow("Regular", now.UtcDateTime.AddDays(1), now.AddHours(-14));
    }
    [Theory]
    [InlineData("bay-2", 6, 2, true)]
    [InlineData("bay-2", 6, 3, false)]
    [InlineData("vip-7", 6, 4, true)]
    [InlineData("vip-7", 7, 0, false)]
    [InlineData("bay-2", 1, -1, false)]
    public void CapacityIncludesNonPlayers(string id, int players, int guests, bool allowed)
    {
        var counts = new[] { new BayParticipants { ResourceId = id, Players = players, NonPlayers = guests } };
        var bay = new ResourceDto(id, "Bay", id == "vip-7" ? "VIP" : "Regular");
        if (allowed)
        {
            BookingRules.ValidateParticipants("Regular", [bay], counts);
        }
        else
        {
            Assert.Throws<ReservationValidationException>(() => BookingRules.ValidateParticipants("Regular", [bay], counts));
        }
    }
    [Fact]
    public void EverySelectedBayNeedsCounts() => Assert.Throws<ReservationValidationException>(() =>
        BookingRules.ValidateParticipants("Regular", [new("bay-2", "Bay 2", "Regular"), new("bay-3", "Bay 3", "Regular")],
            [new BayParticipants { ResourceId = "bay-2", Players = 1 }]));
    [Theory]
    [InlineData(3, 360, 40000)]
    [InlineData(3, 840, 60000)]
    [InlineData(6, 360, 50000)]
    [InlineData(6, 840, 80000)]
    public void DemoQuoteUsesDocumentedRentalRates(int day, int duration, decimal expected) =>
        Assert.Equal(expected, BookingRules.DemoRentalTotal(new DateOnly(2026, 11, day), duration));
    [Fact]
    public void GuestTokensAreBoundToSessionAndSubmission()
    {
        Assert.NotEqual(GuestBookingService.AccessToken("session-a", "key"), GuestBookingService.AccessToken("session-b", "key"));
        Assert.NotEqual(GuestBookingService.AccessToken("session-a", "key"), GuestBookingService.AccessToken("session-a", "other"));
        Assert.NotEqual(GuestBookingService.CodeHash("session", "a@example.test", "123456"), GuestBookingService.CodeHash("other", "a@example.test", "123456"));
    }
    [Fact]
    public async Task DemoPaymentRequiresBothEnvironmentAndExplicitConfiguration()
    {
        var service = new ReservationService(null!, null!, Microsoft.Extensions.Options.Options.Create(new ReservationPolicy { DemoPaymentsEnabled = true }));
        await Assert.ThrowsAsync<ReservationValidationException>(() => service.DemoPaymentAsync("id", "success", false, default));
        service = new ReservationService(null!, null!, Microsoft.Extensions.Options.Options.Create(new ReservationPolicy()));
        await Assert.ThrowsAsync<ReservationValidationException>(() => service.DemoPaymentAsync("id", "success", true, default));
    }
    [Theory]
    [InlineData("Regular", "bay-2", 8, true)]
    [InlineData("Regular", "bay-2", 9, false)]
    [InlineData("VIP", "vip-7", 10, true)]
    [InlineData("VIP", "vip-7", 11, false)]
    [InlineData("Regular", "bay-2", 0, false)]
    public void TotalGuestCountIncludesNonPlayers(string service, string bay, int guests, bool allowed)
    {
        var resource = new ResourceDto(bay, "Bay", service);
        if (allowed) { BookingRules.ValidateGuests(service, [resource], guests); }
        else { Assert.Throws<ReservationValidationException>(() => BookingRules.ValidateGuests(service, [resource], guests)); }
    }

    [Fact]
    public void ConfiguredCapacityUsesCatalogTypeAndLimits()
    {
        var bay = new ResourceDto("studio", "Studio", "VIP") { MaxPlayers = 4, MaxTotalGuests = 9, MaxNonPlayers = 1 };
        BookingRules.ValidateGuests("VIP", [bay], 9);
        Assert.Throws<ReservationValidationException>(() => BookingRules.ValidateGuests("VIP", [bay], 10));
        Assert.Throws<ReservationValidationException>(() => BookingRules.ValidateGuests("KpgaGroup", [bay], 9));
        BookingRules.ValidateParticipants("VIP", [bay], [new() { ResourceId = bay.Id, Players = 4, NonPlayers = 1 }]);
        Assert.Throws<ReservationValidationException>(() => BookingRules.ValidateParticipants("VIP", [bay],
            [new() { ResourceId = bay.Id, Players = 4, NonPlayers = 2 }]));
    }
    [Theory]
    [InlineData("Pending", true)]
    [InlineData("Confirmed", true)]
    [InlineData("Checked-In", true)]
    [InlineData("Expired", false)]
    [InlineData("Cancelled", false)]
    [InlineData("Rejected", false)]
    public void UpcomingListContainsOnlyFutureActiveBookings(string status, bool expected)
    {
        var now = new DateTime(2026, 11, 4, 4, 0, 0, DateTimeKind.Utc);
        var booking = new ReservationDto("id", "ref", "Regular", status, now.AddHours(1), now.AddHours(2), now.AddMinutes(30), ["bay-2"], "First", "Last", "09000000000", 2, []);
        Assert.Equal(expected, booking.IsUpcoming(now));
        Assert.False(booking.IsUpcoming(now.AddHours(3)));
    }
}
