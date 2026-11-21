using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using HoleInHub.Data.Context;
using HoleInHub.Data.Reservations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualBasic.FileIO;
using MongoDB.Driver;
using Xunit;

namespace HoleInHub.Tests.Web.Smoke;

public sealed partial class StaffBlockHttpTests
{
    private const string ReportsPath = "/manager/reports";

    [Theory]
    [InlineData("Staff", HttpStatusCode.Forbidden)]
    [InlineData("Customer", HttpStatusCode.Forbidden)]
    [InlineData("", HttpStatusCode.Unauthorized)]
    public async Task ReportAndExportRequireManagementAccess(string role, HttpStatusCode expected)
    {
        await using var factory = Factory("test_reports_access");
        using var client = Client(factory, role);
        foreach (var path in new[] { ReportsPath, ReportsPath + "/export" })
        {
            using var response = await client.GetAsync(path);
            Assert.Equal(expected, response.StatusCode);
        }
    }

    [Fact, Trait("Category", "Integration")]
    public async Task ReservationReportAndCsvReconcileWithScheduleAcrossManilaMidnight()
    {
        var database = "test_reports_" + Guid.NewGuid().ToString("N");
        await using var factory = Factory(database);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IMongoDbContext>();
        using var manager = Client(factory, "Manager");
        using var admin = Client(factory, "Admin");
        const string range = "?from=2026-11-10&to=2026-11-10";
        var midnight = new DateTime(2026, 11, 9, 16, 0, 0, DateTimeKind.Utc);
        ReservationRecord Booking(int i, DateTime start, DateTime end) => new()
        {
            Id = $"row-{i:D2}",
            Reference = $"HIH-REP-{i:D2}",
            FirstName = i == 0 ? "=José," : "Alex",
            LastName = i == 0 ? "\"Santos\"" : "Santos",
            Service = i is 0 or 1 ? "VIP" : "Regular",
            Status = i is 0 or 2 ? "Cancelled" : "Confirmed",
            StartUtc = start,
            EndUtc = end,
            ResourceIds = ["bay-1", "bay-2"],
            ResourceNames = new() { ["bay-1"] = "East", ["bay-2"] = "West" }
        };
        try
        {
            var rows = Enumerable.Range(1, 20).Select(i => Booking(i, midnight.AddMinutes(i), midnight.AddHours(1))).ToList();
            rows.Add(Booking(0, midnight.AddMinutes(-1), midnight.AddMinutes(1)));
            rows.Add(Booking(21, midnight.AddHours(-1), midnight));
            rows.Add(Booking(22, midnight.AddDays(1), midnight.AddDays(1).AddHours(1)));
            rows.Add(Booking(23, midnight.AddDays(-6), midnight.AddDays(-6).AddHours(1)));
            rows.Add(Booking(24, midnight.AddDays(-29), midnight.AddDays(-29).AddHours(1)));
            await context.Database.GetCollection<ReservationRecord>("reservations").InsertManyAsync(rows);

            static string[] References(string html) =>
                Regex.Matches(html, "HIH-REP-[0-9]{2}").Select(match => match.Value).Distinct().ToArray();
            var first = References(await manager.GetStringAsync(ReportsPath + range));
            var second = References(await admin.GetStringAsync(ReportsPath + range + "&page=2"));
            var schedule = References(await manager.GetStringAsync("/staff/reservations/list" + range))
                .Concat(References(await manager.GetStringAsync("/staff/reservations/list" + range + "&page=2")));
            Assert.NotEmpty(first);
            Assert.NotEmpty(second);
            Assert.Empty(first.Intersect(second));
            Assert.Equal(schedule, first.Concat(second));
            Assert.Equal(21, first.Length + second.Length);

            var full = await ReadReportCsv(admin, range + "&page=2");
            Assert.Equal(first.Concat(second), full.Skip(1).Select(row => row[0]));
            const string filters = range + "&service=VIP&status=Cancelled";
            Assert.Equal(new[] { "HIH-REP-00" }, References(await manager.GetStringAsync(ReportsPath + filters)));
            var filtered = await ReadReportCsv(manager, filters);
            Assert.Equal(new[] { "Reservation ID", "Customer", "Service", "Bays", "Start (Manila)", "End (Manila)", "Status" }, filtered[0]);
            var row = Assert.Single(filtered.Skip(1));
            Assert.Equal("HIH-REP-00", row[0]);
            Assert.Equal("'=José, \"Santos\"", row[1]);
            Assert.Equal("VIP", row[2]);
            Assert.Contains("East", row[3]);
            Assert.Contains("West", row[3]);
            Assert.Equal("2026-11-09 23:59", row[4]);
            Assert.Equal("2026-11-10 00:01", row[5]);
            Assert.Equal("Cancelled", row[6]);
            using var workbookResponse = await manager.GetAsync(ReportsPath + "/export.xlsx" + filters);
            workbookResponse.EnsureSuccessStatusCode();
            Assert.EndsWith(".xlsx", workbookResponse.Content.Headers.ContentDisposition?.FileNameStar);
            using var archive = new ZipArchive(await workbookResponse.Content.ReadAsStreamAsync());
            using var workbookXml = archive.GetEntry("xl/workbook.xml")!.Open();
            var workbook = XDocument.Load(workbookXml);
            XNamespace sheet = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            Assert.Equal(new[] { "Summary", "Status breakdown", "Reservation volume", "Service usage",
                "Reserved bay-hours", "Customer trends", "Reservations" },
                workbook.Descendants(sheet + "sheet").Select(tab => tab.Attribute("name")!.Value));
            using var reservationXml = archive.GetEntry("xl/worksheets/sheet7.xml")!.Open();
            var reservationSheet = XDocument.Load(reservationXml);
            Assert.Equal(2, reservationSheet.Descendants(sheet + "row").Count());
            Assert.Contains(reservationSheet.Descendants(sheet + "t"), cell => cell.Value == row[1][1..]);
            Assert.Empty(reservationSheet.Descendants(sheet + "f"));

            var empty = await ReadReportCsv(manager, "?from=2026-11-12&to=2026-11-12");
            Assert.Equal(filtered[0], Assert.Single(empty));
            var day = await ReadReportCsv(admin, "?preset=1");
            Assert.Equal(full.Skip(1).Select(r => r[0]), day.Skip(1).Select(r => r[0]));
            var week = (await ReadReportCsv(admin, "?preset=7")).Skip(1).Select(r => r[0]).ToArray();
            Assert.Contains("HIH-REP-23", week);
            Assert.DoesNotContain("HIH-REP-24", week);
            Assert.Equal(week, (await ReadReportCsv(admin, "")).Skip(1).Select(r => r[0]));
            var month = await ReadReportCsv(admin, "?preset=30");
            Assert.Contains(month, r => r[0] == "HIH-REP-24");
            Assert.DoesNotContain(month, r => r[0] == "HIH-REP-22");
        }
        finally { await context.Client.DropDatabaseAsync(database); }
    }

    private static async Task<List<string[]>> ReadReportCsv(HttpClient client, string query)
    {
        using var response = await client.GetAsync(ReportsPath + "/export" + query);
        response.EnsureSuccessStatusCode();
        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("attachment", response.Content.Headers.ContentDisposition?.DispositionType);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        using var reader = new StringReader(new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF'));
        using var parser = new TextFieldParser(reader) { HasFieldsEnclosedInQuotes = true };
        parser.SetDelimiters(",");
        var rows = new List<string[]>();
        while (!parser.EndOfData) { rows.Add(parser.ReadFields()!); }
        Assert.Contains(rows, row => row[0].StartsWith("Reservation volume", StringComparison.Ordinal));
        Assert.Contains(rows, row => row[0] == "Reserved bay-hours (Manila hour)");
        Assert.Contains(rows, row => row[0] == "Service usage");
        Assert.Contains(rows, row => row[0] == "Customer trends");
        Assert.Contains(rows, row => row[0] == "Reservation summary");
        return rows.TakeWhile(row => row.Length == 7).ToList();
    }
}
