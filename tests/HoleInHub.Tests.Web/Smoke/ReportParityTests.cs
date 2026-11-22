using System.IO.Compression;
using System.Net;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using HoleInHub.Data.Context;
using HoleInHub.Data.Customers;
using HoleInHub.Data.Reservations;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using Xunit;

namespace HoleInHub.Tests.Web.Smoke;

public sealed partial class StaffBlockHttpTests
{
    [Theory, Trait("Category", "Integration")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReportSummaryChartsCsvAndEveryWorksheetReconcile(bool empty)
    {
        var database = "test_report_parity_" + Guid.NewGuid().ToString("N");
        await using var factory = Factory(database);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IMongoDbContext>();
        using var client = Client(factory, "Manager");
        var midnight = new DateTime(2026, 11, 9, 16, 0, 0, DateTimeKind.Utc);
        ReservationRecord Fixture(string id, string service, string status, double start, double end) => new()
        {
            Id = id,
            Reference = "HIH-" + id,
            FirstName = "Sample",
            LastName = id,
            Service = service,
            Status = status,
            StartUtc = midnight.AddHours(start),
            EndUtc = midnight.AddHours(end),
            ResourceIds = ["east", "west"],
            ResourceNames = new() { ["east"] = "East", ["west"] = "West" }
        };
        try
        {
            await context.Database.GetCollection<ReservationRecord>("reservations").InsertManyAsync([
                Fixture("Prior", "Regular", "Completed", -24, -23),
                Fixture("Overnight", "Regular", "Completed", 23.5, 24.75),
                Fixture("Morning", "VIP", "Confirmed", 56, 57),
                Fixture("Evening", "VIP", "Cancelled", 65, 66),
                Fixture("Absent", "Regular", "No-show", 67, 68)
            ]);
            await context.Database.GetCollection<CustomerMembershipRecord>(CustomerProfilePersistence.MembershipsName).InsertManyAsync([
                new() { Id = "Prior", ProfileId = "returning" }, new() { Id = "Overnight", ProfileId = "returning" },
                new() { Id = "Morning", ProfileId = "new" }, new() { Id = "Evening", ProfileId = "new" }
            ]);
            var from = empty ? "2026-11-15" : "2026-11-10";
            var to = empty ? "2026-11-17" : "2026-11-12";
            var query = $"?from={from}&to={to}";
            var html = WebUtility.HtmlDecode(await client.GetStringAsync(ReportsPath + query));
            var csv = (await ReadReportCsv(client, query, true)).Select(row => string.Join('|', row)).ToArray();
            var bytes = await client.GetByteArrayAsync(ReportsPath + "/export.xlsx" + query);
            var tabs = ReadWorksheets(bytes);
            var count = empty ? 0 : 4;
            string[] details = empty ? [] : [
                "HIH-Overnight|Sample Overnight|Regular|East, West|2026-11-10 23:30|2026-11-11 00:45|Completed",
                "HIH-Morning|Sample Morning|VIP|East, West|2026-11-12 08:00|2026-11-12 09:00|Confirmed",
                "HIH-Evening|Sample Evening|VIP|East, West|2026-11-12 17:00|2026-11-12 18:00|Cancelled",
                "HIH-Absent|Sample Absent|Regular|East, West|2026-11-12 19:00|2026-11-12 20:00|No-show"
            ];
            string[] volume = empty ? ["2026-11-15|0", "2026-11-16|0", "2026-11-17|0"]
                : ["2026-11-10|1", "2026-11-11|0", "2026-11-12|3"];
            string[] usage = empty ? [] : ["Regular|2", "VIP|2"];
            var hours = Enumerable.Range(0, 24).Select(i => $"{i:00}:00|" + (empty ? "0" :
                i switch { 0 => "1.5", 8 => "2", 23 => "1", _ => "0" })).ToArray();
            var customers = new[] { "New customers", "Returning customers", "Reservations without a matched customer" }
                .Select(label => label + "|" + (empty ? 0 : 1)).ToArray();
            string[] statuses = empty ? ["Cancelled|0|0", "Completed|0|0", "No-show|0|0", "Pending|0|0"]
                : ["Cancelled|1|25", "Completed|1|25", "Confirmed|1|25", "No-show|1|25", "Pending|0|0"];
            Assert.Equal(new[] { $"From (Manila)|{from}", $"To (Manila)|{to}", "Service|All services",
                "Reservation list status|All statuses", $"Total reservations|{count}", $"Matching reservations|{count}" }, tabs["Summary"]);
            Assert.Equal(statuses, tabs["Status breakdown"]);
            Assert.Equal(volume, tabs["Reservation volume"]);
            Assert.Equal(usage, tabs["Service usage"]);
            Assert.Equal(hours, tabs["Reserved bay-hours"]);
            Assert.Equal(customers, tabs["Customer trends"]);
            Assert.Equal(details, tabs["Reservations"]);
            Assert.Equal(7, tabs.Count);
            Assert.Equal(details, csv.Skip(1).TakeWhile(line => line.StartsWith("HIH-")));
            Assert.Equal(new[] { $"From|{from}", $"To|{to}", "Service|All services", "Reservation list status|All statuses" },
                Between("Report filters (Manila)|Value", "Reservation summary|Count|Share of total"));
            Assert.Equal(new[] { $"Total reservations|{count}", $"Matching reservations|{count}" }.Concat(statuses.Select(value => value + "%")),
                Between("Reservation summary|Count|Share of total", "Reservation volume (Manila date)|Reservations"));
            Assert.Equal(volume, Between("Reservation volume (Manila date)|Reservations", "Service usage|Reservations"));
            Assert.Equal(usage, Between("Service usage|Reservations", "Reserved bay-hours (Manila hour)|Bay-hours"));
            Assert.Equal(hours, Between("Reserved bay-hours (Manila hour)|Bay-hours", "Customer trends|Count"));
            Assert.Equal(customers, Between("Customer trends|Count", null));

            Assert.Matches($"Total reservations</dt><dd><a[^>]*>{count}</a>", html);
            foreach (var status in new[] { "Completed", "Cancelled", "No-show" })
            {
                Assert.Contains($"aria-label=\"{(empty ? 0 : 1)} {status} reservations\"", html);
            }
            if (empty)
            {
                Assert.Contains("No reservations in this period", html);
                Assert.DoesNotContain("data-report-values", html);
            }
            else
            {
                Assert.Equal(volume, Chart("Reservation volume"));
                Assert.Equal(usage, Chart("Service usage"));
                Assert.Equal(hours, Chart("Reserved bay-hours"));
                Assert.Contains("1 new customers and 1 returning customers", html);
                Assert.Contains("1 reservation has no matched customer", html);
                Assert.Equal(details.Select(line => line.Split('|')[0]), Regex.Matches(html, @">(?<ref>HIH-\w+)</a>")
                    .Select(match => match.Groups["ref"].Value));
                if (Environment.GetEnvironmentVariable("HOLEINHUB_SAMPLE_REPORT_XLSX") is { Length: > 0 } path)
                {
                    await File.WriteAllBytesAsync(path, bytes);
                }
            }

            IEnumerable<string> Between(string header, string? next)
            {
                Assert.Contains(header, csv);
                return csv.SkipWhile(row => row != header).Skip(1).TakeWhile(row => row != next);
            }

            IEnumerable<string> Chart(string title)
            {
                var table = Regex.Match(html, $"<h2>{title}</h2>(.*?)</table>", RegexOptions.Singleline).Groups[1].Value;
                return Regex.Matches(table, """<th scope="row">([^<]+)</th><td data-value="([^"]+)">""")
                    .Select(match => match.Groups[1].Value + "|" + match.Groups[2].Value);
            }
        }
        finally { await context.Client.DropDatabaseAsync(database); }
    }

    private static Dictionary<string, string[]> ReadWorksheets(byte[] bytes)
    {
        using var archive = new ZipArchive(new MemoryStream(bytes));
        XNamespace sheet = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace relation = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        var targets = Xml("xl/_rels/workbook.xml.rels").Root!.Elements().ToDictionary(
            item => (string)item.Attribute("Id")!, item => (string)item.Attribute("Target")!);
        return Xml("xl/workbook.xml").Descendants(sheet + "sheet").ToDictionary(tab => (string)tab.Attribute("name")!,
            tab => Xml("xl/" + targets[(string)tab.Attribute(relation + "id")!]).Descendants(sheet + "row").Skip(1)
                .Select(row => string.Join('|', row.Elements(sheet + "c").Select(cell => cell.Value))).ToArray());

        XDocument Xml(string name)
        {
            using var file = archive.GetEntry(name)!.Open();
            return XDocument.Load(file);
        }
    }
}
