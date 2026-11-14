using HoleInHub.Business.Reservations;
using Microsoft.AspNetCore.WebUtilities;

namespace HoleInHubReservationManagementSubsystem.ViewModels;

public sealed class DashboardQuery
{
    public string Mode { get; set; } = "today";
    public DateOnly? Date { get; set; }
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
    public string? Preset { get; set; }

    public (DateOnly From, DateOnly To) Resolve(DateOnly today)
    {
        if (Mode is not ("today" or "trends"))
        {
            throw new ReservationValidationException("Choose Today or Trends.");
        }

        var to = Mode == "today" ? Date ?? today : To ?? today;
        DashboardMetrics.ValidateRange(to, to);
        var from = Mode == "today" ? to : Preset switch
        {
            "7" => to.AddDays(-6), "30" => to.AddDays(-29), null or "" or "custom" => From ?? to.AddDays(-6),
            _ => throw new ReservationValidationException("Choose a valid date preset.")
        };
        DashboardMetrics.ValidateRange(from, to);
        return (from, to);
    }
}

public sealed record DashboardViewModel(DashboardSnapshot Data, string Mode, bool CanManage)
{
    public IReadOnlyList<HoleInHub.Business.Activity.ActivityEntry> LatestActivity { get; init; } = [];
    private Dictionary<string, string?> Query => new()
    {
        ["mode"] = Mode, ["date"] = Data.From.ToString("yyyy-MM-dd"),
        ["from"] = Data.From.ToString("yyyy-MM-dd"), ["to"] = Data.To.ToString("yyyy-MM-dd")
    };
    public string DashboardUrl => QueryHelpers.AddQueryString("/staff/dashboard", Query);
    public string SliceUrl(string kind, string? value = null, string? group = null, int page = 1)
    {
        var query = Query; query["kind"] = kind; query["value"] = value; query["group"] = group; query["page"] = page.ToString();
        return QueryHelpers.AddQueryString("/staff/dashboard/bookings", query);
    }
}

public sealed record DashboardTrend(double Current, double Previous, string Period, bool IncreaseIsGood = true)
{
    public double Difference => Current - Previous;
    public string Tone => Difference == 0 ? "neutral" : (Difference > 0) == IncreaseIsGood ? "positive" : "negative";
    public string Change => Difference == 0 ? "No change" : Previous == 0 ? "New" : $"{Math.Abs(Difference / Previous * 100):0.#}%";
}

public sealed record DashboardBookingsViewModel(DashboardViewModel Context, DashboardSlice Slice, string Kind, string? Value, string? Group, int Page)
{
    public string Title => Kind switch
    {
        "day" => $"Sessions starting {Value}", "service" => $"{DashboardMetrics.ServiceLabel(Value ?? "")} bookings",
        "hour" => $"Bookings overlapping {Value}:00–{int.Parse(Value!) + 1}:00", _ => "Scheduled reservations"
    };
}
