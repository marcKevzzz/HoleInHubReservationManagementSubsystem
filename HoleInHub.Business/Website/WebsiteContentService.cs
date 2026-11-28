using System.Text.Json;
using HoleInHub.Data.Website;

namespace HoleInHub.Business.Website;

public sealed class WebsiteContentService(WebsiteContentPersistence persistence)
{
    public const int MaxImageBytes = 10 * 1024 * 1024;
    public const string ImagePrefix = "/website-content/images/";
    public static IReadOnlyList<string> OfferCategories { get; } = ["Rate Guide", "Coaching", "Junior Golf"];

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static WebsiteContentRecord InitialContent() =>
        JsonSerializer.Deserialize<WebsiteContentRecord>(WebsiteContentSeed.Json, JsonOptions)!;

    public async Task<WebsiteContentRecord?> ReadAsync(CancellationToken ct)
    {
        var json = await persistence.ReadAsync(ct);
        return json is null ? null : JsonSerializer.Deserialize<WebsiteContentRecord>(json, JsonOptions);
    }

    public async Task<(byte[] Bytes, string ContentType)?> ReadImageAsync(string id, bool admin, CancellationToken ct)
    {
        if (!Guid.TryParseExact(id, "N", out _))
        {
            return null;
        }

        if (!admin)
        {
            var content = await ReadAsync(ct);
            if (content is null || !content.Gallery.Concat(content.Section.Visibility == "Published" ? content.Offers : [])
                .Any(item => item.Visibility == "Published" && item.Image == ImagePrefix + id))
            {
                return null;
            }
        }
        var image = await persistence.ReadImageAsync(id, ct);
        return image is null ? null : (image.Bytes, image.ContentType);
    }

    public async Task<WebsiteContentRecord> SaveAsync(WebsiteContentRecord submitted, string? imageTarget, byte[]? bytes, CancellationToken ct)
    {
        var current = await ReadAsync(ct) ??
            throw new WebsiteContentUnavailableException();
        if (current.Revision != submitted.Revision)
        {
            throw new WebsiteContentConflictException();
        }

        var categories = submitted.GalleryCategories;
        Require(categories is { Length: > 0 and <= 100 }, "Keep between 1 and 100 gallery categories.");
        foreach (var category in categories!)
        {
            Text(category, 60, "Category name");
            Require(!string.Equals(category, "All", StringComparison.OrdinalIgnoreCase), "All is reserved for the gallery filter.");
        }
        Require(categories!.Distinct(StringComparer.OrdinalIgnoreCase).Count() == categories!.Length,
            "Category names must be unique.");
        Require(submitted.Gallery is { Length: <= 500 } && submitted.Offers is { Length: <= 100 },
            "Keep at most 500 gallery items and 100 offers.");

        var section = submitted.Section;
        Require(section is not null, "Section settings are required.");
        Text(section!.Subtitle, 60, "Section subtitle");
        Text(section.Heading, 100, "Section heading");
        Text(section.Introduction, 500, "Section introduction");
        Visibility(section.Visibility);

        WebsiteImage? upload = null;
        if (bytes is not null)
        {
            Require(!string.IsNullOrEmpty(imageTarget), "Select an item for the uploaded image.");
            Require(bytes.Length is > 0 and <= MaxImageBytes, "Choose an image up to 10 MB.");
            upload = new WebsiteImage(Guid.NewGuid().ToString("N"), ImageType(bytes), bytes);
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        var usedUpload = false;
        WebsiteItem[] ValidateItems(WebsiteItem[] items, WebsiteItem[] previousItems, IReadOnlyList<string> allowedCategories, bool offer)
        {
            var previousById = previousItems.ToDictionary(item => item.Id, StringComparer.Ordinal);
            return items.Select(item =>
            {
                Require(item is not null, "An item is missing.");
                var id = item!.Id;
                Text(id, 64, "Item identifier");
                previousById.TryGetValue(id, out var previous);
                Require(previous is not null || Guid.TryParseExact(id, "D", out _), "An item identifier is invalid.");
                Require(ids.Add(id), "Item identifiers must be unique.");
                Text(item.Title, 120, "Title");
                Text(item.Description, 600, "Description");
                Require(allowedCategories.Contains(item.Category, StringComparer.Ordinal), "Choose an existing category.");
                Visibility(item.Visibility);

                if (offer)
                {
                    Text(item.OverlayTitle, 100, "Hover overlay title");
                    Text(item.OverlayDescription, 400, "Hover overlay description");
                    Text(item.ModalTitle, 120, "Modal title");
                    Text(item.ModalDescription, 600, "Modal description");
                    Require(item.ButtonVisibility is "visible" or "hidden", "Choose a button visibility.");
                    Text(item.ButtonText, 60, "Button text", item.ButtonVisibility == "visible");
                    Text(item.ButtonDestination, 500, "Button destination", item.ButtonVisibility == "visible");
                    Require(item.ButtonDestination.Length == 0 || IsValidDestination(item.ButtonDestination),
                        "Enter a site path or an HTTP(S) URL.");
                }

                var image = item.Image;
                if (upload is not null && item.Id == imageTarget)
                {
                    image = ImagePrefix + upload.Id;
                    usedUpload = true;
                }
                else
                {
                    Require(image == "" || image == previous?.Image, "Upload an image through the image picker.");
                }

                return item with
                {
                    Image = image!,
                    Video = previous?.Video ?? "",
                    CaptionCategory = previous?.Category == item.Category ? previous.CaptionCategory : "",
                    ImageAlt = image == previous?.Image ? previous!.ImageAlt : "",
                    Width = image == previous?.Image ? previous!.Width : 1122,
                    Height = image == previous?.Image ? previous!.Height : 1402,
                    PromoPeriod = previous?.PromoPeriod ?? "",
                    CompactPreview = previous?.CompactPreview == true
                };
            }).ToArray();
        }

        var next = current with
        {
            Revision = current.Revision + 1,
            GalleryCategories = categories!,
            Gallery = ValidateItems(submitted.Gallery!, current.Gallery, categories!, false),
            Offers = ValidateItems(submitted.Offers!, current.Offers, OfferCategories, true),
            Section = section
        };
        Require(upload is null || usedUpload, "The item selected for the upload is missing.");

        var retained = next.Gallery.Concat(next.Offers).Select(item => item.Image).ToHashSet(StringComparer.Ordinal);
        var obsolete = current.Gallery.Concat(current.Offers).Select(item => item.Image)
            .Where(path => path.StartsWith(ImagePrefix, StringComparison.Ordinal) && !retained.Contains(path))
            .Select(path => path[ImagePrefix.Length..]).Distinct().ToArray();
        if (!await persistence.SaveAsync(next.Revision, JsonSerializer.Serialize(next, JsonOptions), upload, obsolete, ct))
        {
            throw new WebsiteContentConflictException();
        }

        return next;
    }

    private static void Visibility(string? value) => Require(value is "Published" or "Hidden", "Choose a visibility.");

    private static void Text(string? value, int max, string field, bool required = true)
    {
        Require(value is not null && value.Length <= max && value == value.Trim() && (!required || value.Length > 0),
            required ? $"{field} must contain 1 to {max} characters." : $"{field} must contain at most {max} characters.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new ArgumentException(message);
        }
    }

    internal static bool IsValidDestination(string value)
    {
        if (value.Any(char.IsControl) || value.Contains('\\'))
        {
            return false;
        }

        if (value.StartsWith('/') && !value.StartsWith("//", StringComparison.Ordinal))
        {
            return true;
        }

        return Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http" && uri.Host.Length > 0;
    }

    private static string ImageType(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 33 && bytes[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) &&
            bytes[12..16].SequenceEqual("IHDR"u8))
        {
            return "image/png";
        }

        if (bytes.Length >= 12 && bytes[0] == 255 && bytes[1] == 216 && bytes[2] == 255 &&
            bytes[^2] == 255 && bytes[^1] == 217)
        {
            return "image/jpeg";
        }

        if (bytes.Length >= 14 && (bytes.StartsWith("GIF87a"u8) || bytes.StartsWith("GIF89a"u8)) &&
            bytes[^1] == 0x3b)
        {
            return "image/gif";
        }

        if (bytes.Length >= 20 && bytes.StartsWith("RIFF"u8) && bytes[8..12].SequenceEqual("WEBP"u8))
        {
            return "image/webp";
        }

        if (bytes.Length >= 16 && bytes[4..8].SequenceEqual("ftyp"u8) &&
            (bytes[8..12].SequenceEqual("avif"u8) || bytes[8..12].SequenceEqual("avis"u8)))
        {
            return "image/avif";
        }

        throw new ArgumentException("Choose a valid PNG, JPEG, WebP, AVIF, or GIF image.");
    }
}

public sealed class WebsiteContentConflictException() : Exception("Content changed in another session. Refresh before saving again.");
public sealed class WebsiteContentUnavailableException() : Exception("Website content storage is unavailable. Try again after the website content migration is applied.");
