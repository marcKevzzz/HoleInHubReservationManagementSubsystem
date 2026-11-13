using HoleInHub.Data.Services;

namespace HoleInHub.Business.Services;

public sealed record ServiceCatalogItem(string Id, string Title, string ImagePath, string Summary,
    string RateText, string Details, string ActionLabel, string ActionUrl)
{
    public int? MaxPax { get; init; }
    public string[] Images { get; init; } = [];
    public string DepositText { get; init; } = "";
}

public interface IServiceCatalog
{
    Task<IReadOnlyList<ServiceCatalogItem>> GetActiveAsync(CancellationToken ct);
}

public sealed partial class ServiceCatalog(ServiceCatalogPersistence persistence) : IServiceCatalog
{
    public async Task<IReadOnlyList<ServiceCatalogItem>> GetActiveAsync(CancellationToken ct)
    {
        var records = await persistence.GetActiveAsync(ct);
        return records.Select(x => new ServiceCatalogItem(x.Id, x.Title,
            x.ImagePath.StartsWith("/media/", StringComparison.Ordinal) && !x.ImagePath.Contains("..") && !x.ImagePath.Contains('\\')
                ? x.ImagePath : "/media/facebook/venue-overview.webp",
            x.Summary, x.RateText, x.Details,
            x.BookingService == "WholeShop" || x.DepositType != "None" || x.Action == "Contact" ? "Contact the venue" : x.ActionLabel,
            x.BookingService == "WholeShop" || x.DepositType != "None" || x.Action != "Book" ? "/Home/Contacts"
                : "/Reservations/Create?service=" + Uri.EscapeDataString(x.BookingService))
        { MaxPax = x.MaxPax, Images = ImagePaths(x), DepositText = FormatDeposit(x.DepositType, x.DepositValue) }).ToArray();
    }
}
