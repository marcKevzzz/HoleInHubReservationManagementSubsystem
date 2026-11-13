using System.Security.Claims;
using HoleInHub.Business.Accounts;
using HoleInHub.Business.Services;
using HoleInHubReservationManagementSubsystem.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace HoleInHubReservationManagementSubsystem.Controllers;

[Authorize(Policy = HoleInHub.Web.Accounts.StaffAuthorization.Workspace, Roles = StaffRoles.Admin)]
[AutoValidateAntiforgeryToken, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("admin/services")]
public sealed class ServiceManagementController(ServiceCatalog catalog) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct) => View(await catalog.GetAllAsync(ct));
    [HttpGet("new")]
    public IActionResult Create() => View("Edit", new ServiceEditorForm());
    [HttpGet("{id}/edit")]
    public async Task<IActionResult> Edit(string id, CancellationToken ct) =>
        await catalog.GetAsync(id, ct) is { } item ? View(ServiceEditorForm.From(item)) : NotFound();

    [HttpPost("reorder")]
    public async Task<IActionResult> Reorder(string positions, CancellationToken ct)
    {
        try
        {
            var rows = System.Text.Json.JsonSerializer.Deserialize<ServicePosition[]>(positions ?? "[]", new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
            await catalog.ReorderAsync(rows, User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "", ct);
            return Json(new { rows = await catalog.GetAllAsync(ct) });
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or System.Text.Json.JsonException)
        {
            return Conflict(new { error = "Could not save the order. Reload services and try again." });
        }
    }
    [HttpPost("save"), RequestSizeLimit(25165824), RequestFormLimits(MultipartBodyLengthLimit = 25165824)]
    public async Task<IActionResult> Save(ServiceEditorForm form, CancellationToken ct)
    {
        if (ModelState.IsValid)
        {
            try
            {
                if (form.Uploads.Count > 8 || form.Uploads.Any(file => file.Length > 4194304)
                    || form.Uploads.Sum(file => file.Length) > 20 * 1024 * 1024)
                {
                    throw new ArgumentException("Choose up to eight images, 4 MB each and 20 MB in total.");
                }

                var uploads = new List<byte[]>();
                foreach (var file in form.Uploads)
                {
                    using var stream = new MemoryStream();
                    await file.CopyToAsync(stream, ct);
                    uploads.Add(stream.ToArray());
                }
                await catalog.SaveAsync(form.Command(), uploads, User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "", ct);
                new ToastNotice("success", form.Create ? "Service added" : "Service updated",
                    form.Active ? "Published on the customer services page." : "Saved as inactive.").Store(TempData);
                if (Request.Headers.Accept == "application/json")
                {
                    return Json(new { redirect = Url.Action(nameof(Index)) });
                }

                return RedirectToAction(nameof(Index));
            }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException)
            {
                if (Request.Headers.Accept == "application/json")
                {
                    return UnprocessableEntity(new { error = error.Message });
                }

                ModelState.AddModelError("", error.Message);
            }
        }
        if (Request.Headers.Accept == "application/json")
        {
            return UnprocessableEntity(new { error = "Check the highlighted fields and try again.", fields = ModelState.Where(pair => pair.Value?.Errors.Count > 0).ToDictionary(pair => pair.Key, pair => pair.Value!.Errors[0].ErrorMessage) });
        }

        return View("Edit", form);
    }
    [HttpPost("{id}/state")]
    public async Task<IActionResult> State(string id, long revision, string commandId, string operation, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest();
        }

        try
        {
            await catalog.SetStateAsync(id, revision, commandId, operation, User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "", ct);
            new ToastNotice("success", operation switch { "activate" => "Service activated", "delete" => "Service deleted", _ => "Service deactivated" }).Store(TempData);
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException)
        {
            if (Request.Headers.Accept == "application/json")
            {
                return Conflict(new { error = error.Message });
            }

            new ToastNotice("error", "Service not changed", error.Message).Store(TempData);
        }
        if (Request.Headers.Accept == "application/json")
        {
            return Json(new { redirect = Url.Action(nameof(Index)) });
        }

        return RedirectToAction(nameof(Index));
    }
}

[AllowAnonymous]
public sealed class ServiceMediaController(ServiceCatalog catalog) : Controller
{
    [HttpGet("/media/services/{id}")]
    public async Task<IActionResult> Image(string id, CancellationToken ct)
    {
        var image = await catalog.ImageAsync(id, ct);
        if (image is null)
        {
            return NotFound();
        }

        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers.CacheControl = "public,max-age=31536000,immutable";
        return File(image.Content, image.ContentType);
    }
}
