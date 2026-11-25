using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using HoleInHub.Business.Reservations;

namespace HoleInHubReservationManagementSubsystem.ViewModels;

internal static class ReportWorkbook
{
    private static readonly XNamespace Sheet = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace Relationship = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace Package = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace Content = "http://schemas.openxmlformats.org/package/2006/content-types";

    public static byte[] Create(ReservationReport report, IReadOnlyList<DashboardBooking> matches, DateOnly from,
        DateOnly to, string? service, string? status)
    {
        var statuses = report.Rows.GroupBy(row => row.Status).ToDictionary(group => group.Key, group => group.Count());
        (string Name, object[] Headers, IEnumerable<object[]> Rows)[] tabs =
        [
            ("Summary", ["Metric", "Value"], [
                ["From (Manila)", Date(from)], ["To (Manila)", Date(to)],
                ["Service", service is null ? "All services" : report.Services.GetValueOrDefault(service, service)],
                ["Reservation list status", status ?? "All statuses"],
                ["Total reservations", report.Rows.Count], ["Matching reservations", matches.Count]]),
            ("Status breakdown", ["Status", "Reservations", "Percent of total"],
                statuses.Keys.Concat(["Completed", "Cancelled", "No-show", "Pending"]).Distinct().Order()
                    .Select(key => new object[] {key, statuses.GetValueOrDefault(key),
                        report.Rows.Count == 0 ? 0d : Math.Round(100d * statuses.GetValueOrDefault(key) / report.Rows.Count, 2)})),
            ("Reservation volume", [from == to ? "Hour (Manila)" : "Date (Manila)", "Reservations"],
                report.Volume.Select(point => new object[] {point.Key, point.Value})),
            ("Service usage", ["Service", "Reservations"],
                report.ServiceCounts.OrderByDescending(point => point.Value).ThenBy(point => point.Key)
                    .Select(point => new object[] {point.Key, point.Value})),
            ("Reserved bay-hours", ["Hour (Manila)", "Bay-hours"],
                report.BayHours.Select(point => new object[] {point.Key, point.Value})),
            ("Customer trends", ["Metric", "Count"], [["New customers", report.NewCustomers],
                ["Returning customers", report.ReturningCustomers], ["Reservations without a matched customer", report.UnmatchedReservations]]),
            ("Reservations", ["Reservation ID", "Customer", "Service", "Bays", "Start (Manila)", "End (Manila)", "Status"],
                matches.Select(row => new object[] {row.Reference, row.Guest, row.Service, string.Join(", ", row.Bays),
                    Local(row.StartUtc), Local(row.EndUtc), row.Status}))
        ];

        if (matches.Any(row => row.PackageDates.Length > 0))
        {
            tabs = [.. tabs, ("Package lessons", ["Reservation ID", "Included dates (Manila)", "Learners marked present"],
                matches.Where(row => row.PackageDates.Length > 0).Select(row => new object[] { row.Reference,
                    string.Join(" · ", row.PackageDates.Select(Local)), row.AttendedLessons }))];
        }

        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            var indexes = Enumerable.Range(1, tabs.Length).ToArray();
            Write("[Content_Types].xml", new XElement(Content + "Types",
                new XElement(Content + "Default", new XAttribute("Extension", "rels"), new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
                Override("/xl/workbook.xml", "sheet.main"),
                indexes.Select(i => Override($"/xl/worksheets/sheet{i}.xml", "worksheet"))));
            Write("_rels/.rels", new XElement(Package + "Relationships", Rel(1, "officeDocument", "xl/workbook.xml")));
            Write("xl/_rels/workbook.xml.rels", new XElement(Package + "Relationships", indexes.Select(i => Rel(i, "worksheet", $"worksheets/sheet{i}.xml"))));
            Write("xl/workbook.xml", new XElement(Sheet + "workbook", new XElement(Sheet + "bookViews", new XElement(Sheet + "workbookView")),
                new XElement(Sheet + "sheets",
                indexes.Select(i => new XElement(Sheet + "sheet", new XAttribute("name", tabs[i - 1].Name),
                    new XAttribute("sheetId", i), new XAttribute(Relationship + "id", $"rId{i}"))))));
            foreach (var i in indexes)
            {
                var tab = tabs[i - 1];
                Write($"xl/worksheets/sheet{i}.xml", new XElement(Sheet + "worksheet",
                    new XElement(Sheet + "sheetViews", new XElement(Sheet + "sheetView", new XAttribute("workbookViewId", 0),
                        new XElement(Sheet + "pane", new XAttribute("ySplit", 1), new XAttribute("topLeftCell", "A2"),
                            new XAttribute("activePane", "bottomLeft"), new XAttribute("state", "frozen")))),
                    new XElement(Sheet + "cols", new XElement(Sheet + "col", new XAttribute("min", 1),
                        new XAttribute("max", tab.Headers.Length), new XAttribute("width", 29), new XAttribute("customWidth", 1))),
                    new XElement(Sheet + "sheetData", Row(tab.Headers), tab.Rows.Select(Row))));
            }

            void Write(string path, XElement xml)
            {
                using var stream = archive.CreateEntry(path).Open();
                xml.Save(stream);
            }
        }

        return output.ToArray();
    }

    private static XElement Row(object[] values) => new(Sheet + "row", values.Select(value => value is string text
        ? new XElement(Sheet + "c", new XAttribute("t", "inlineStr"), new XElement(Sheet + "is",
            new XElement(Sheet + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), text)))
        : new XElement(Sheet + "c", new XElement(Sheet + "v", value))));

    private static XElement Rel(int id, string kind, string target) => new(Package + "Relationship",
        new XAttribute("Id", $"rId{id}"), new XAttribute("Type", $"{Relationship}/{kind}"), new XAttribute("Target", target));

    private static XElement Override(string path, string type) => new(Content + "Override",
        new XAttribute("PartName", path), new XAttribute("ContentType", $"application/vnd.openxmlformats-officedocument.spreadsheetml.{type}+xml"));

    private static string Date(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    private static string Local(DateTime utc) => DashboardMetrics.Local(utc).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
}
