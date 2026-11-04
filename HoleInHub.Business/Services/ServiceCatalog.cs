using HoleInHub.Data.Services;

namespace HoleInHub.Business.Services;

public sealed record ServiceCatalogItem(string Id, string Title, string ImagePath, string Summary,
    string RateText, string Details, string ActionLabel, string ActionUrl);

public interface IServiceCatalog
{
    Task<IReadOnlyList<ServiceCatalogItem>> GetActiveAsync(CancellationToken ct);
}

public sealed class ServiceCatalog(ServiceCatalogPersistence persistence) : IServiceCatalog
{
    public async Task<IReadOnlyList<ServiceCatalogItem>> GetActiveAsync(CancellationToken ct)
    {
        var records = await persistence.GetActiveAsync(ct);
        return records.Select(x => new ServiceCatalogItem(x.Id, x.Title,
            x.ImagePath.StartsWith("/media/", StringComparison.Ordinal) && !x.ImagePath.Contains("..") && !x.ImagePath.Contains('\\')
                ? x.ImagePath : "/media/facebook/venue-overview.webp",
            x.Summary, x.RateText, x.Details,
            x.BookingService == "WholeShop" || x.Action == "Contact" ? "Contact the venue" : x.ActionLabel,
            x.BookingService == "WholeShop" || x.Action != "Book" ? "/Home/Contacts"
                : "/Reservations/Create?service=" + Uri.EscapeDataString(x.BookingService))).ToArray();
    }
}
