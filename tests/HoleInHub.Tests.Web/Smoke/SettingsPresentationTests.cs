using HoleInHub.Business.Services;
using HoleInHub.Business.Settings;
using HoleInHubReservationManagementSubsystem.Controllers;
using HoleInHubReservationManagementSubsystem.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace HoleInHub.Tests.Web.Smoke;

public sealed class SettingsPresentationTests
{
    [Fact]
    public void EmptyPoliciesReceiveDraftsWithoutPublishingOrReplacingSavedText()
    {
        var settings = new SettingsEdit { BookingPolicy = "Venue-approved wording" };
        var form = SettingsForm.From(settings);
        Assert.Equal(settings.BookingPolicy, form.BookingPolicy);
        Assert.Contains("INFORMATION AND PURPOSES", form.PrivacyPolicy);
        Assert.Contains("OPENING HOURS AND CAPACITY", form.VenueRules);
        Assert.False(form.PoliciesPublished);
        Assert.Equal("", settings.PrivacyPolicy);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public async Task HealthReportsReadinessWithoutExposingInternalErrors(bool ready, bool throws)
    {
        var result = Assert.IsType<JsonResult>(await new SettingsController(null!).Health(new HealthStub(ready, throws), default));
        var json = System.Text.Json.JsonSerializer.Serialize(result.Value);
        Assert.Contains($"\"ready\":{ready.ToString().ToLowerInvariant()}", json);
        Assert.DoesNotContain("private connection", json);
    }

    private sealed class HealthStub(bool ready, bool throws) : IHealthService
    {
        public Task<bool> CheckDatabaseLivenessAsync(CancellationToken ct = default) => Task.FromResult(ready);
        public Task<(bool IsReady, string StatusDescription)> CheckDatabaseReadinessAsync(CancellationToken ct = default) =>
            throws ? throw new InvalidOperationException("private connection") : Task.FromResult((ready, "private connection"));
    }
}
