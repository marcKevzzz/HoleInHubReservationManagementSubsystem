using System.Globalization;
using System.Text;
using HoleInHub.Business.Accounts;
using HoleInHub.Business.Reservations;
using HoleInHubReservationManagementSubsystem.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HoleInHubReservationManagementSubsystem.Controllers;

public sealed partial class LeadershipController
{
    [Authorize(Roles = StaffRoles.VenueManagement), HttpGet("/manager/reports")]
    public Task<IActionResult> Reports([FromQuery] ReportQuery query, CancellationToken ct) => LoadReport(query, null, ct);

    [Authorize(Roles = StaffRoles.VenueManagement), HttpGet("/manager/reports/export")]
    public Task<IActionResult> ExportReservations([FromQuery] ReportQuery query, CancellationToken ct) => LoadReport(query, "csv", ct);

    [Authorize(Roles = StaffRoles.VenueManagement), HttpGet("/manager/reports/export.xlsx")]
    public Task<IActionResult> ExportWorkbook([FromQuery] ReportQuery query, CancellationToken ct) => LoadReport(query, "xlsx", ct);

    private async Task<IActionResult> LoadReport(ReportQuery query, string? export, CancellationToken ct)
    {
        var (from, to, preset, service, status, page) = query;
        if (!ModelState.IsValid || from.HasValue != to.HasValue || preset is not (null or 1 or 7 or 30)
            || service?.Length > 100 || status?.Length > 100 || page < 1)
        {
            return BadRequest("Choose a valid report period and filters.");
        }
        var end = to ?? Today;
        var start = from ?? end.AddDays(1 - (preset ?? 7));
        try
        {
            if (export is null && Request.Headers["X-Reports"] == "list")
            {
                var list = await reservations.GetReportListAsync(start, end, service, status, page, ct);
                return PartialView("_ReportList", new ReportListViewModel(start, end, service, status, list.Page, list.Pages, list.Rows, list.Counts));
            }
            var report = await reservations.GetReportAsync(start, end, service, ct);
            var matches = string.IsNullOrEmpty(status) ? report.Rows : report.Rows.Where(x => x.Status == status).ToArray();
            if (export == "xlsx")
            {
                return File(ReportWorkbook.Create(report, matches, start, end, service, status),
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"report_{start:yyyy-MM-dd}_to_{end:yyyy-MM-dd}.xlsx");
            }
            if (export == "csv")
            {
                return File(ReportCsv(report, matches, start, end, service, status), "text/csv; charset=utf-8", $"reservations_{start:yyyy-MM-dd}_to_{end:yyyy-MM-dd}.csv");
            }
            var pages = Math.Max(1, (matches.Count + 19) / 20);
            page = Math.Min(page, pages);
            var model = new ReportsViewModel(start, end, service, status, page, pages, matches.Count,
                matches.Skip((page - 1) * 20).Take(20).ToArray(), report);
            return Request.Headers["X-Reports"] == "true" ? PartialView("_ReportsContent", model) : View("Reports", model);
        }
        catch (ReservationValidationException error) { return BadRequest(error.Message); }
    }

    private static byte[] ReportCsv(ReservationReport report, IReadOnlyList<DashboardBooking> rows,
        DateOnly from, DateOnly to, string? service, string? status)
    {
        var csv = new StringBuilder("\uFEFFReservation ID,Customer,Service,Bays,Start (Manila),End (Manila),Status\r\n");
        foreach (var row in rows)
        {
            string[] fields = [row.Reference, row.Guest, row.Service, string.Join(", ", row.Bays),
                LocalTime(row.StartUtc), LocalTime(row.EndUtc), row.Status];
            csv.AppendJoin(",", fields.Select(Escape)).Append("\r\n");
        }
        Section("Report filters (Manila)", "Value");
        Write("From", from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Write("To", to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Write("Service", service is null ? "All services" : report.Services.GetValueOrDefault(service, service));
        Write("Reservation list status", status ?? "All statuses");

        Section("Reservation summary", "Count", "Share of total");
        Write("Total reservations", Number(report.Rows.Count));
        Write("Matching reservations", Number(rows.Count));
        var statuses = report.Rows.GroupBy(row => row.Status).ToDictionary(group => group.Key, group => group.Count());
        foreach (var category in statuses.Keys.Concat(["Completed", "Cancelled", "No-show", "Pending"]).Distinct().Order())
        {
            var count = statuses.GetValueOrDefault(category);
            Write(category, Number(count), report.Rows.Count == 0 ? "0%" :
                (count / (double)report.Rows.Count).ToString("0.#%", CultureInfo.InvariantCulture));
        }

        Section(from == to ? "Reservation volume (Manila hour)" : "Reservation volume (Manila date)", "Reservations");
        foreach (var point in report.Volume) { Write(point.Key, Number(point.Value)); }

        Section("Service usage", "Reservations");
        foreach (var point in report.ServiceCounts.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key))
        {
            Write(point.Key, Number(point.Value));
        }

        Section("Reserved bay-hours (Manila hour)", "Bay-hours");
        foreach (var point in report.BayHours) { Write(point.Key, Number(point.Value)); }

        Section("Customer trends", "Count");
        Write("New customers", Number(report.NewCustomers));
        Write("Returning customers", Number(report.ReturningCustomers));
        Write("Reservations without a matched customer", Number(report.UnmatchedReservations));

        return Encoding.UTF8.GetBytes(csv.ToString());

        void Section(params string[] columns)
        {
            csv.Append("\r\n");
            Write(columns);
        }

        void Write(params string[] values) => csv.AppendJoin(",", values.Select(Escape)).Append("\r\n");
        static string Number(double value) => value.ToString(CultureInfo.InvariantCulture);

        static string LocalTime(DateTime utc) => DashboardMetrics.Local(utc).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        static string Escape(string value)
        {
            var trimmed = value.AsSpan().TrimStart();
            if ((trimmed.Length > 0 && "=+-@".Contains(trimmed[0])) || value.StartsWith('\t') || value.StartsWith('\r') || value.StartsWith('\n'))
            {
                value = "'" + value;
            }
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
    }
}
