using System.Text.Json;
using HoleInHub.Business.Reservations;
using Xunit;
namespace HoleInHub.Tests.Unit.Reservations;

public sealed class StaffBookingSerializationTests
{
    [Fact]
    public void UnlinkedRequestsKeepTheExistingIdempotencyPayload()
    {
        var request = new StaffBookingRequest("bay-2", new DateOnly(2026, 11, 20), new TimeOnly(13, 0), 30, "Alex", "Santos", "09175550123", 2, "Walk-in", "key");
        var original = JsonSerializer.Serialize(new { request.ResourceId, request.Date, request.StartTime, request.DurationMinutes, request.FirstName, request.LastName, request.Phone, request.Attendees, request.Source, request.IdempotencyKey });
        Assert.Equal(original, JsonSerializer.Serialize(request));
        Assert.Contains("CustomerProfileId", JsonSerializer.Serialize(request with { CustomerProfileId = "cp_example" }));
    }
}
