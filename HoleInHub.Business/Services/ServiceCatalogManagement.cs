using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using HoleInHub.Data.Services;
namespace HoleInHub.Business.Services;

public sealed record ServiceMedia(byte[] Content, string ContentType);
public sealed record ServiceEdit(string Id, long Revision, string CommandId, bool Create, string Title, string Summary,
    string RateText, string Details, string BookingService, string Action, string ActionLabel, int Order, bool Active,
    string DepositType, decimal DepositValue, string[] Images)
{
    public int? MaxPax { get; init; }
    public string[] ImageOrder { get; init; } = [];
}
public sealed partial class ServiceCatalog
{
    public static IReadOnlyList<string> BookingTypes { get; } = ["Regular", "VIP", "PhilippineCoach", "KpgaPro", "KpgaGroup", "WholeShop"];
    public static string[] ImagePaths(ServiceCatalogRecord record) =>
        (record.Images.Length > 0 ? record.Images : [record.ImagePath]).Where(ValidImagePath).Distinct().ToArray();
    private static bool ValidImagePath(string path) => path.StartsWith("/media/", StringComparison.Ordinal)
        && !path.Contains("..") && !path.Contains('\\') && !path.Contains('?') && !path.Contains('#') && !path.Any(char.IsControl);
    public static string FormatDeposit(string type, decimal value) => type switch
    {
        "Peso" => value.ToString("C", CultureInfo.GetCultureInfo("en-PH")) + " deposit",
        "Percentage" => value.ToString("0.##", CultureInfo.InvariantCulture) + "% deposit",
        _ => ""
    };
    private static ServiceEdit Edit(ServiceCatalogRecord record) => new(record.Id, record.Revision,
        Guid.NewGuid().ToString("N"), false, record.Title, record.Summary, record.RateText, record.Details,
        record.BookingService, record.Action, record.ActionLabel, record.Order, record.Active,
        record.DepositType, record.DepositValue, ImagePaths(record))
    { MaxPax = record.MaxPax };
    public async Task<IReadOnlyList<ServiceEdit>> GetAllAsync(CancellationToken ct) =>
        (await persistence.GetAllAsync(ct)).Select(Edit).ToArray();
    public async Task<ServiceEdit?> GetAsync(string id, CancellationToken ct) =>
        await persistence.GetAsync(id, ct) is { } record ? Edit(record) : null;
    public async Task<ServiceMedia?> ImageAsync(string id, CancellationToken ct)
    {
        if (!Guid.TryParseExact(id, "N", out _))
        {
            return null;
        }

        var image = await persistence.ImageAsync(id, ct);
        return image is null ? null : new(image.Content, image.ContentType);
    }
    public static void Validate(ServiceEdit edit)
    {
        if (edit.Id.Length is < 1 or > 80 || edit.Id.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-')
            || edit.Revision < 0 || !Guid.TryParseExact(edit.CommandId, "N", out _))
        {
            throw new ArgumentException("Reload the service and try again.");
        }

        if (string.IsNullOrWhiteSpace(edit.Title) || edit.Title.Length > 120 || string.IsNullOrWhiteSpace(edit.Summary) || edit.Summary.Length > 500
            || string.IsNullOrWhiteSpace(edit.RateText) || edit.RateText.Length > 160 || string.IsNullOrWhiteSpace(edit.Details) || edit.Details.Length > 12000
            || string.IsNullOrWhiteSpace(edit.ActionLabel) || edit.ActionLabel.Length > 60 || edit.Order is < 0 or > 9999)
        {
            throw new ArgumentException("Complete the service title, summary, rate, description and button label within their limits.");
        }

        if (!BookingTypes.Contains(edit.BookingService) || edit.Action is not ("Book" or "Contact"))
        {
            throw new ArgumentException("Choose a supported booking type and customer action.");
        }

        if (edit.DepositType is not ("None" or "Peso" or "Percentage") || edit.DepositType != "None"
            && (edit.DepositValue <= 0 || edit.DepositValue > (edit.DepositType == "Percentage" ? 100 : 1000000)
                || decimal.Round(edit.DepositValue, 2) != edit.DepositValue))
        {
            throw new ArgumentException("Enter a deposit above zero, up to 100% or ₱1,000,000, with at most two decimal places.");
        }

        if (edit.MaxPax is < 1 or > 80)
        {
            throw new ArgumentException("Max pax must be between 1 and 80.");
        }
        if (edit.Images.Length > 8 || edit.Images.Any(path => !ValidImagePath(path)))
        {
            throw new ArgumentException("Choose up to eight valid service images.");
        }
    }
    public static string ImageType(byte[] bytes)
    {
        if (bytes.Length is < 12 or > 4194304)
        {
            throw new ArgumentException("Each image must be a JPEG, PNG or WebP up to 4 MB.");
        }

        if (bytes[0] == 0xff && bytes[1] == 0xd8 && bytes[2] == 0xff)
        {
            return "image/jpeg";
        }

        if (bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
        {
            return "image/png";
        }

        if (System.Text.Encoding.ASCII.GetString(bytes, 0, 4) == "RIFF" && System.Text.Encoding.ASCII.GetString(bytes, 8, 4) == "WEBP")
        {
            return "image/webp";
        }

        throw new ArgumentException("Choose JPEG, PNG or WebP images.");
    }
    public async Task SaveAsync(ServiceEdit edit, IReadOnlyList<byte[]> uploads, string actor, CancellationToken ct)
    {
        Validate(edit);
        if (string.IsNullOrWhiteSpace(actor))
        {
            throw new ArgumentException("Sign in again before saving.");
        }

        var prior = edit.Create ? null : await persistence.GetAsync(edit.Id, ct) ?? throw new ArgumentException("This service is no longer available.");
        var existing = prior is null ? [] : ImagePaths(prior);
        if (edit.Images.Any(path => !existing.Contains(path)))
        {
            throw new ArgumentException("Reload the existing service images.");
        }

        if (edit.Images.Length + uploads.Count is < 1 or > 8 || uploads.Sum(bytes => (long)bytes.Length) > 20 * 1024 * 1024)
        {
            throw new ArgumentException("Keep at least one image and at most eight. Upload up to 20 MB in one save.");
        }

        var images = uploads.Select(bytes =>
        {
            var hash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(edit.Id).Concat(bytes).ToArray());
            return new ServiceImageRecord
            {
                Id = Convert.ToHexString(hash)[..32].ToLowerInvariant(),
                ContentType = ImageType(bytes),
                Content = bytes
            };
        }).ToArray();
        var paths = edit.Images.Concat(images.Select(image => "/media/services/" + image.Id)).Distinct().ToArray();
        if (edit.ImageOrder.Length > 0)
        {
            var ordered = edit.ImageOrder.Select(key => key.StartsWith("upload:", StringComparison.Ordinal)
                && int.TryParse(key[7..], out var index) && index >= 0 && index < images.Length
                    ? "/media/services/" + images[index].Id : key).ToArray();
            if (ordered.Length != paths.Length || ordered.Distinct().Count() != paths.Length || ordered.Any(path => !paths.Contains(path)))
            {
                throw new ArgumentException("Reload the image order and try again.");
            }
            paths = ordered;
        }
        var commandHash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { edit, paths, actor })));
        var record = new ServiceCatalogRecord
        {
            MaxPax = edit.MaxPax,
            Id = edit.Id,
            Title = edit.Title.Trim(),
            Summary = edit.Summary.Trim(),
            Details = edit.Details.Trim(),
            RateText = edit.RateText.Trim(),
            BookingService = edit.BookingService,
            Action = edit.Action,
            ActionLabel = edit.ActionLabel.Trim(),
            Order = edit.Order,
            Active = edit.Active,
            Images = paths,
            ImagePath = paths[0],
            DepositType = edit.DepositType,
            DepositValue = edit.DepositType == "None" ? 0 : edit.DepositValue,
            CommandId = edit.CommandId,
            CommandHash = commandHash,
            UpdatedBy = actor,
            UpdatedUtc = DateTime.UtcNow
        };
        await persistence.SaveAsync(record, edit.Revision, edit.Create, images, ct);
    }
    public async Task SetStateAsync(string id, long revision, string commandId, string operation, string actor, CancellationToken ct)
    {
        if (!Guid.TryParseExact(commandId, "N", out _) || operation is not ("activate" or "deactivate" or "delete") || string.IsNullOrWhiteSpace(actor))
        {
            throw new ArgumentException("Reload the service and choose a valid action.");
        }

        var record = await persistence.GetAsync(id, ct);
        if (record is null)
        {
            if (operation == "delete")
            {
                return;
            }

            throw new ArgumentException("This service is no longer available.");
        }
        record.Active = operation == "activate";
        record.Deleted = operation == "delete";
        record.CommandId = commandId;
        record.CommandHash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { id, revision, commandId, operation, actor })));
        record.UpdatedBy = actor;
        record.UpdatedUtc = DateTime.UtcNow;
        await persistence.SaveAsync(record, revision, false, [], ct);
    }
}
