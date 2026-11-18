using HoleInHub.Business.Activity;
using Microsoft.AspNetCore.WebUtilities;
namespace HoleInHubReservationManagementSubsystem.ViewModels;

public sealed class ActivityQuery
{
    public string Range { get; set; } = "week";
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
    public string? Search { get; set; }
    public string Category { get; set; } = "All";
    public string Actor { get; set; } = "everyone";
    public string? Cursor { get; set; }
    public int Page { get; set; } = 1;
    public DateTime? AsOf { get; set; }
    public string Url(string path = "/staff/activity", bool reset = false, string? actor = null, string? cursor = null) => QueryHelpers.AddQueryString(path, new Dictionary<string, string?>
    {
        ["range"] = Range,
        ["from"] = From?.ToString("yyyy-MM-dd"),
        ["to"] = To?.ToString("yyyy-MM-dd"),
        ["search"] = string.IsNullOrWhiteSpace(Search) ? null : Search,
        ["category"] = Category,
        ["actor"] = actor ?? Actor,
        ["cursor"] = reset ? null : cursor ?? Cursor,
        ["page"] = reset ? null : (cursor is not null ? Math.Clamp(Page, 1, 100000) + 1 : Math.Clamp(Page, 1, 100000)).ToString(),
        ["asOf"] = reset ? null : AsOf?.ToString("O")
    });
}
public sealed record ActivityListModel(ActivityQuery Query, ActivityPage? Page, bool IncludeAccess, string? Error = null)
{
    public bool Embedded { get; init; }
}
public sealed record ActivityDetailModel(ActivityEntry Entry, string BackUrl, string? TargetUrl, string TargetAction);
