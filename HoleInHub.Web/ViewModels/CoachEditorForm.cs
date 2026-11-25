using System.ComponentModel.DataAnnotations;
using HoleInHub.Business.Reservations;

namespace HoleInHubReservationManagementSubsystem.ViewModels;

public sealed class CoachEditorForm
{
    public string? Id { get; set; }
    public long? Revision { get; set; }
    [Required, StringLength(100)] public string Name { get; set; } = "";
    [StringLength(100)] public string? Title { get; set; }
    [Phone, StringLength(30)] public string? Phone { get; set; }
    [EmailAddress, StringLength(254)] public string? Email { get; set; }
    public bool Active { get; set; } = true;
    public List<CoachWeekRow> Weekly { get; set; } = [];
    public List<CoachDateRow> Exceptions { get; set; } = [];

    public IEnumerable<ValidationResult> ValidateSchedule()
    {
        var windows = new List<(string Group, string Label, string Prefix, TimeOnly Start, TimeOnly End)>();
        for (var i = 0; i < Weekly.Count; i++)
        {
            var row = Weekly[i];
            var prefix = $"Weekly[{i}]";
            var label = $"Shift {i + 1} ({row.Day})";
            foreach (var error in ValidatePair(prefix, label, row.Start, row.End)) { yield return error; }
            if (row.Start is { } start && row.End is { } end && start < end)
            {
                windows.Add((row.Day.ToString(), label, prefix, start, end));
            }
        }
        for (var i = 0; i < Exceptions.Count; i++)
        {
            var row = Exceptions[i];
            var prefix = $"Exceptions[{i}]";
            var label = row.Date is { } date ? $"Exception {i + 1} ({date:d MMM yyyy})" : $"Exception {i + 1}";
            if (row.Date is null && (row.Start.HasValue || row.End.HasValue))
            {
                yield return new($"{label} needs a date.", [$"{prefix}.Date"]);
            }
            foreach (var error in ValidatePair(prefix, label, row.Start, row.End)) { yield return error; }
            if (row.Date is null) { continue; }
            if (!row.Start.HasValue && !row.End.HasValue && Exceptions.Count(x => x.Date == row.Date) > 1)
            {
                yield return new($"{label} marks the day off. Remove the other exceptions on this date or enter a shift here.", [$"{prefix}.Date"]);
            }
            if (row.Start is { } start && row.End is { } end && start < end)
            {
                windows.Add((row.Date.Value.ToString("yyyy-MM-dd"), label, prefix, start, end));
            }
        }

        foreach (var group in windows.GroupBy(x => x.Group))
        {
            var ordered = group.OrderBy(x => x.Start).ToArray();
            var previous = ordered[0];
            foreach (var window in ordered.Skip(1))
            {
                if (window.Start < previous.End)
                {
                    yield return new($"{window.Label} overlaps {previous.Label}. Adjust the shift times.", [$"{window.Prefix}.Start"]);
                }
                if (window.End > previous.End) { previous = window; }
            }
        }
    }

    private static IEnumerable<ValidationResult> ValidatePair(string prefix, string label, TimeOnly? start, TimeOnly? end)
    {
        if (start.HasValue != end.HasValue)
        {
            var missing = start.HasValue ? "End" : "Start";
            yield return new($"{label} needs a {missing.ToLowerInvariant()} time.", [$"{prefix}.{missing}"]);
        }
        else if (start.HasValue && end <= start)
        {
            yield return new($"{label} must end later than it starts on the same day.", [$"{prefix}.End"]);
        }
    }

    public static CoachEditorForm From(CoachDto? coach)
    {
        var form = new CoachEditorForm();
        if (coach is not null)
        {
            form.Id = coach.Id;
            form.Revision = coach.Revision;
            form.Name = coach.Name;
            form.Title = coach.Title;
            form.Phone = coach.Phone;
            form.Email = coach.Email;
            form.Active = coach.Active;
            form.Weekly = coach.WeeklyHours.Select(x => new CoachWeekRow
            { Day = x.Day, Start = TimeOnly.MinValue.AddMinutes(x.StartMinute), End = TimeOnly.MinValue.AddMinutes(x.EndMinute) }).ToList();
            form.Exceptions = coach.Overrides.Select(x => new CoachDateRow
            { Date = DateOnly.ParseExact(x.Date, "yyyy-MM-dd"), Start = Time(x.StartMinute), End = Time(x.EndMinute) }).ToList();
        }
        foreach (var day in Enum.GetValues<DayOfWeek>())
        {
            if (!form.Weekly.Any(x => x.Day == day))
            {
                form.Weekly.Add(new() { Day = day });
            }
        }

        form.Weekly = form.Weekly.OrderBy(x => ((int)x.Day + 6) % 7).ToList();
        form.Exceptions.Add(new());
        return form;
    }

    private static TimeOnly? Time(int? minute) => minute.HasValue ? TimeOnly.MinValue.AddMinutes(minute.Value) : null;
}

public sealed class CoachWeekRow
{
    public DayOfWeek Day { get; set; }
    public TimeOnly? Start { get; set; }
    public TimeOnly? End { get; set; }
}

public sealed class CoachDateRow
{
    public DateOnly? Date { get; set; }
    public TimeOnly? Start { get; set; }
    public TimeOnly? End { get; set; }
}
