using System.Text.Json;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace HoleInHubReservationManagementSubsystem.ViewModels;

public sealed record ToastNotice(string Kind, string Title, string? Message = null, string? Href = null, string? Action = null)
{
    private const string Key = "ToastNotice";

    public void Store(ITempDataDictionary data) => data[Key] = JsonSerializer.Serialize(this);

    public static ToastNotice? Take(ITempDataDictionary data) =>
        data[Key] is string value ? JsonSerializer.Deserialize<ToastNotice>(value) : null;
}
