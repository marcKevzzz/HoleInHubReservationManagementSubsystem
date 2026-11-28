using System.Text.Json;
using HoleInHub.Business.Accounts;
using HoleInHub.Business.Services;
using HoleInHub.Business.Website;
using HoleInHub.Web.Accounts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HoleInHubReservationManagementSubsystem.Controllers;

[Authorize(Policy = StaffAuthorization.Workspace, Roles = StaffRoles.Admin)]
[AutoValidateAntiforgeryToken, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("staff/gallery-content/data")]
public sealed class WebsiteContentController(WebsiteContentService content) : Controller
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const int MaxRequestBytes = WebsiteContentService.MaxImageBytes + 3 * 1024 * 1024;

    [HttpGet]
    public async Task<IActionResult> Read(CancellationToken ct)
    {
        try
        {
            var state = await content.ReadAsync(ct) ?? throw new WebsiteContentUnavailableException();
            return Json(new { state, offerCategories = WebsiteContentService.OfferCategories });
        }
        catch (Exception error) when (error is WebsiteContentUnavailableException || ServiceAvailability.Translate(error) is not null)
        {
            return Problem("Website content storage is unavailable. Confirm that migration 032 has been applied.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    [HttpPut]
    [RequestSizeLimit(MaxRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxRequestBytes, ValueLengthLimit = 2 * 1024 * 1024)]
    public async Task<IActionResult> Save([FromForm] string? state, [FromForm] string? imageTarget, [FromForm] IFormFile? image, CancellationToken ct)
    {
        if (!ModelState.IsValid || string.IsNullOrWhiteSpace(state))
        {
            return BadRequest(new { detail = "The content request is missing or invalid." });
        }

        try
        {
            var submitted = JsonSerializer.Deserialize<WebsiteContentRecord>(state, JsonOptions);
            if (submitted is null)
            {
                return BadRequest(new { detail = "The content request is invalid." });
            }

            byte[]? bytes = null;
            if (image is not null)
            {
                if (image.Length is <= 0 or > WebsiteContentService.MaxImageBytes)
                {
                    return BadRequest(new { detail = "Choose an image up to 10 MB." });
                }

                using var stream = new MemoryStream((int)image.Length);
                await image.CopyToAsync(stream, ct);
                bytes = stream.ToArray();
            }
            return Json(await content.SaveAsync(submitted, imageTarget, bytes, ct));
        }
        catch (JsonException) { return BadRequest(new { detail = "The content request is invalid." }); }
        catch (ArgumentException error) { return BadRequest(new { detail = error.Message }); }
        catch (WebsiteContentConflictException error) { return Conflict(new { detail = error.Message }); }
        catch (Exception error) when (error is WebsiteContentUnavailableException || ServiceAvailability.Translate(error) is not null)
        {
            return Problem("Website content storage is unavailable. Try again shortly.", statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }
}
