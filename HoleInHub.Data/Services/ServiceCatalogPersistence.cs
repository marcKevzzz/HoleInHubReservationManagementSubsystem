using HoleInHub.Data.Context;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace HoleInHub.Data.Services;

public sealed class ServiceCatalogRecord
{
    [BsonId] public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string[] Images { get; set; } = [];
    public int? MaxPax { get; set; }
    public bool Deleted { get; set; }
    public string DepositType { get; set; } = "None";
    [BsonRepresentation(MongoDB.Bson.BsonType.Decimal128)] public decimal DepositValue { get; set; }
    public long Revision { get; set; }
    public string CommandId { get; set; } = "";
    public string CommandHash { get; set; } = "";
    public string UpdatedBy { get; set; } = "";
    public DateTime? UpdatedUtc { get; set; }
    public string ImagePath { get; set; } = "";
    public string Summary { get; set; } = "";
    public string RateText { get; set; } = "";
    [BsonIgnoreIfNull] public ServicePricing? Pricing { get; set; }
    public string Details { get; set; } = "";
    public string BookingService { get; set; } = "";
    public string BookingTypeName { get; set; } = "";
    [BsonIgnoreIfNull] public ServiceAvailabilityRule[]? AvailabilityRules { get; set; }
    public string Action { get; set; } = "Book";
    public string ActionLabel { get; set; } = "Request a bay";
    public int Order { get; set; }
    public bool Active { get; set; }
}

public sealed partial class ServiceCatalogPersistence(IMongoDbContext context)
{
    public Task<ServiceCatalogRecord?> GetBookingServiceAsync(string id, CancellationToken ct) =>
        context.Database.GetCollection<ServiceCatalogRecord>("service_catalog")
            .Find(service => service.Id == id).FirstOrDefaultAsync(ct)!;
    public async Task<IReadOnlyList<ServiceCatalogRecord>> GetActiveAsync(CancellationToken ct) =>
        await context.Database.GetCollection<ServiceCatalogRecord>("service_catalog")
            .Find(x => x.Active && !x.Deleted).SortBy(x => x.Order).ThenBy(x => x.Id).ToListAsync(ct);
}
