using System.Net;
using System.Text.RegularExpressions;
using HoleInHub.Data.Context;
using HoleInHub.Data.Reservations;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using Xunit;

namespace HoleInHub.Tests.Web.Smoke;

public sealed partial class StaffBlockHttpTests
{
    [Theory]
    [InlineData("", HttpStatusCode.Unauthorized)]
    [InlineData("Customer", HttpStatusCode.Forbidden)]
    public async Task ScheduleListRequiresWorkspaceAccess(string role, HttpStatusCode expected)
    {
        await using var factory = Factory("test_schedule_list_access");
        using var client = Client(factory, role);
        Assert.Equal(expected, (await client.GetAsync("/staff/reservations/list")).StatusCode);
    }

    [Fact, Trait("Category", "Integration")]
    public async Task ScheduleListPagesAllStatusesAcrossManilaDatesAndRestoresThePageFromDetails()
    {
        var database = "test_schedule_list_" + Guid.NewGuid().ToString("N");
        await using var factory = Factory(database);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IMongoDbContext>();
        using var client = Client(factory, "Staff");
        const string range = "/staff/reservations/list?from=2026-11-10&to=2026-11-11";
        try
        {
            var emptyTimeline = await client.GetStringAsync("/staff?date=2026-11-10");
            Assert.Contains("There are no bays", emptyTimeline);
            Assert.Contains("aria-label=\"Timeline\" title=\"Timeline\"", emptyTimeline);
            Assert.Contains("aria-label=\"List\" title=\"List\"", emptyTimeline);
            Assert.Contains("No reservations in this date range", await client.GetStringAsync(range));
            Assert.Contains("Choose a valid start date", await client.GetStringAsync("/staff/reservations/list?from=nope&to=2026-11-11"));
            Assert.Contains("Choose a start date on or before", await client.GetStringAsync("/staff/reservations/list?from=2026-11-12&to=2026-11-11"));

            var start = new DateTime(2026, 11, 9, 16, 0, 0, DateTimeKind.Utc);
            var end = start.AddDays(2);
            string[] statuses = ["Pending", "Confirmed", "Rejected", "Cancelled", "Completed", "Checked-In", "No-show", "Expired"];
            ReservationRecord Booking(int i, DateTime from, DateTime to) => new()
            {
                Id = $"row-{i:D2}",
                Reference = $"HIH-LIST-{i:D2}",
                FirstName = "Alex",
                LastName = "Santos",
                Service = "Regular",
                Status = statuses[i % statuses.Length],
                StartUtc = from,
                EndUtc = to,
                ReviewDeadlineUtc = end,
                ResourceIds = ["bay-1", "bay-2"],
                ResourceNames = new() { ["bay-1"] = "Bay One" }
            };
            var rows = Enumerable.Range(1, 19).Reverse().Select(i => Booking(i, start.AddHours(14), start.AddHours(15))).ToList();
            rows.Add(Booking(0, start.AddHours(-1), start.AddMinutes(1)));
            rows.Add(Booking(20, end.AddMinutes(-1), end.AddHours(1)));
            rows.Add(Booking(21, start.AddHours(-1), start));
            rows.Add(Booking(22, end, end.AddHours(1)));
            await context.Database.GetCollection<ReservationRecord>("reservations").InsertManyAsync(rows);

            var first = WebUtility.HtmlDecode(await client.GetStringAsync(range));
            Assert.Contains("21 reservations", first);
            Assert.Equal(Enumerable.Range(0, 20).Select(i => $"HIH-LIST-{i:D2}"),
                Regex.Matches(first, "HIH-LIST-[0-9]{2}").Select(match => match.Value));
            Assert.Contains("Bay One, bay-2", first);
            Assert.Contains("9 Nov 2026, 11:00 PM", first);
            foreach (var status in statuses) { Assert.Contains($"data-status=\"{status}\"", first); }
            Assert.Matches("(?s)HIH-LIST-00</a>(?:(?!</tr>).)*data-status=\"Expired\"", first);

            var second = WebUtility.HtmlDecode(await client.GetStringAsync(range + "&page=2"));
            Assert.Contains("Page 2 of 2", second);
            Assert.Equal("HIH-LIST-20", Assert.Single(Regex.Matches(second, "HIH-LIST-[0-9]{2}")).Value);
            Assert.Contains("12 Nov 2026, 1:00 AM", second);
            var detailLink = Regex.Match(second, "href=\"([^\"]+)\">HIH-LIST-20</a>").Groups[1].Value;
            var details = WebUtility.HtmlDecode(await client.GetStringAsync(detailLink));
            Assert.Contains($"href=\"{range}&page=2\"", details);
            Assert.Contains("Back to schedule list", details);

            var annual = await client.GetStringAsync("/staff/reservations/list?from=2026-01-01&to=2026-12-31");
            Assert.Contains("23 reservations", annual);
        }
        finally { await context.Client.DropDatabaseAsync(database); }
    }
}
