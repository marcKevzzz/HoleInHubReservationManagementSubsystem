namespace HoleInHub.Data.Services;

public sealed record ServiceAvailabilityRule(int? Weekday, string? Date, int StartMinute, int EndMinute, bool FixedSession, bool Closed);
