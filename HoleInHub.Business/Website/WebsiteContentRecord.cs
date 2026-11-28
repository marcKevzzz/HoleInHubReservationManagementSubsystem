namespace HoleInHub.Business.Website;

public sealed record WebsiteContentRecord
{
    public long Revision { get; init; }
    public string[] GalleryCategories { get; init; } = [];
    public WebsiteItem[] Gallery { get; init; } = [];
    public WebsiteItem[] Offers { get; init; } = [];
    public WebsiteSection Section { get; init; } = new();
}

public sealed record WebsiteItem
{
    public string Id { get; init; } = "";
    public string Title { get; init; } = "";
    public string Category { get; init; } = "";
    public string Description { get; init; } = "";
    public string Visibility { get; init; } = "";
    public string Image { get; init; } = "";
    public string Video { get; init; } = "";
    public string ImageAlt { get; init; } = "";
    public string CaptionCategory { get; init; } = "";
    public int Width { get; init; } = 1122;
    public int Height { get; init; } = 1402;
    public string PromoPeriod { get; init; } = "";
    public bool CompactPreview { get; init; }
    public string OverlayTitle { get; init; } = "";
    public string OverlayDescription { get; init; } = "";
    public string ButtonText { get; init; } = "";
    public string ButtonDestination { get; init; } = "";
    public string ButtonVisibility { get; init; } = "";
    public string ModalTitle { get; init; } = "";
    public string ModalDescription { get; init; } = "";
}

public sealed record WebsiteSection
{
    public string Subtitle { get; init; } = "";
    public string Heading { get; init; } = "";
    public string Introduction { get; init; } = "";
    public string Visibility { get; init; } = "";
}
