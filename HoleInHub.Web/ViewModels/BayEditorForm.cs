using HoleInHub.Business.Reservations;

namespace HoleInHubReservationManagementSubsystem.ViewModels;

public sealed class BayEditorForm
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public bool? Active { get; set; }
    public string Service { get; set; } = "Regular";
    public int? MaxPlayers { get; set; }
    public int? MaxTotalGuests { get; set; }
    public int? MaxNonPlayers { get; set; }
    public long? ExpectedRevision { get; set; }
    public string? CapacityAcknowledgement { get; set; }
    public bool EditPresentation { get; set; }
    public string? Location { get; set; }
    public string? Description { get; set; }
    public string[] Images { get; set; } = [];
    public string[] ImageOrder { get; set; } = [];
    public List<IFormFile> Uploads { get; set; } = [];

    public async Task<BayPresentationEdit?> PresentationAsync(string[] legacyImages, CancellationToken ct)
    {
        if (!EditPresentation) { return null; }
        var files = await EditorImageUploads.ReadAsync(Uploads, ct);
        return new BayPresentationEdit(Location ?? "", Description ?? "", Images, ImageOrder, files)
        { LegacyImages = legacyImages };
    }
}
