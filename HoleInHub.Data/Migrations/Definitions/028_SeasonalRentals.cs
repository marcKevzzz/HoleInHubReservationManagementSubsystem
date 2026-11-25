using HoleInHub.Data.Services;
using MongoDB.Bson;
using MongoDB.Driver;

namespace HoleInHub.Data.Migrations.Definitions;

public sealed class Migration_028_SeasonalRentals : IMongoMigration
{
    public string MigrationId => "028_SeasonalRentals";
    public string Description => "Retain the legacy rental and seed three seasonal rental drafts";
    public string CalculateChecksum() => MongoMigrationChecksum.Compute(MigrationId + ":v1");
    public async Task UpAsync(IMongoDatabase database, CancellationToken ct)
    {
        var catalog = database.GetCollection<ServiceCatalogRecord>("service_catalog");
        foreach (var (id, title, low, high, order) in new[] {
            (SeasonalRental.Whole, "Christmas · Whole Shop", 30000, 40000, 6),
            (SeasonalRental.Regular, "Christmas · Whole Regular Bay", 20000, 25000, 7),
            (SeasonalRental.Vip, "Christmas · VIP (2 Bays)", 12000, 14000, 8) })
        {
            var rental = new ServiceCatalogRecord
            {
                Id = id,
                Title = title,
                Order = order,
                BookingService = "WholeShop",
                ImagePath = "/media/facebook/family-play.webp",
                ActionLabel = "Reserve package",
                Summary = "Eight-hour rental with karaoke, PlayStation gaming, and party games.",
                Details = "Eight continuous hours. Friday to Sunday and designated holidays use the higher rate.",
                DepositType = "Percentage",
                DepositValue = 50,
                RateText = $"₱{low:N0}–₱{high:N0} / eight hours",
                Pricing = new()
                {
                    BaseRate = low,
                    WholeDayRate = low,
                    RentalOverrides = new[] { DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday }
                        .Select(day => new RentalRateOverride(day, null, high, high)).ToArray()
                }
            };
            await catalog.UpdateOneAsync(x => x.Id == id, new BsonDocument("$setOnInsert", rental.ToBsonDocument()),
                new UpdateOptions { IsUpsert = true }, ct);
        }
        await catalog.UpdateOneAsync(x => x.Id == "WholeShop" && x.Active,
            Builders<ServiceCatalogRecord>.Update.Set(x => x.Active, false).Inc(x => x.Revision, 1), cancellationToken: ct);
    }
}
