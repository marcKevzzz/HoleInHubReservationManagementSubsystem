using HoleInHub.Data.Context;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace HoleInHub.Data.Services;

public sealed class ServiceCatalogRecord
{
    [BsonId] public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string ImagePath { get; set; } = "";
    public string Summary { get; set; } = "";
    public string RateText { get; set; } = "";
    public string Details { get; set; } = "";
    public string BookingService { get; set; } = "";
    public string Action { get; set; } = "Book";
    public string ActionLabel { get; set; } = "Request a bay";
    public int Order { get; set; }
    public bool Active { get; set; }
}

public sealed class ServiceCatalogPersistence(IMongoDbContext context)
{
    public async Task<IReadOnlyList<ServiceCatalogRecord>> GetActiveAsync(CancellationToken ct) =>
        await context.Database.GetCollection<ServiceCatalogRecord>("service_catalog")
            .Find(x => x.Active).SortBy(x => x.Order).ThenBy(x => x.Id).ToListAsync(ct);
}
