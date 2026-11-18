namespace HoleInHub.Data.Activity;

public sealed record ActivityQuery(DateTime FromUtc, DateTime ToUtc, DateTime AsOfUtc, string? Category,
    string? ActorId, string? Search, ActivityPosition? After);
public sealed record ActivityPosition(DateTime OccurredUtc, string Key);

// Only approved display fields cross the data boundary; never return source documents.
public sealed class ActivityRecord
{
    public string Key { get; set; } = "";
    public DateTime OccurredUtc { get; set; }
    public string Category { get; set; } = "";
    public string EventType { get; set; } = "";
    public string Actor { get; set; } = "";
    public string ActorLabel { get; set; } = "";
    public string TargetLabel { get; set; } = "";
    public string TargetId { get; set; } = "";
    public string TargetKind { get; set; } = "";
    public DateTime? TargetStartUtc { get; set; }
    public string? Reason { get; set; }
    public string? PreviousStatus { get; set; }
    public string? Status { get; set; }
    public DateTime? BeforeStartUtc { get; set; }
    public DateTime? AfterStartUtc { get; set; }
    public DateTime? BeforeEndUtc { get; set; }
    public DateTime? AfterEndUtc { get; set; }
    public string[] BeforeResources { get; set; } = [];
    public string[] AfterResources { get; set; } = [];
    public string[] PreviousRoles { get; set; } = [];
    public string[] NewRoles { get; set; } = [];
    public string? SourceName { get; set; }
    public string? TargetName { get; set; }
    public int? ReservationCount { get; set; }
}
