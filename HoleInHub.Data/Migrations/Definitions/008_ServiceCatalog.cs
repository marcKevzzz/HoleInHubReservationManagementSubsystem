using HoleInHub.Data.Services;
using MongoDB.Bson;
using MongoDB.Driver;

namespace HoleInHub.Data.Migrations.Definitions;

public sealed class Migration_008_ServiceCatalog : IMongoMigration
{
    public string MigrationId => "008_ServiceCatalog";
    public string Description => "Persist the six existing indicative service offerings and public booking links";
    public string CalculateChecksum() => MongoMigrationChecksum.Compute(MigrationId + ":v1:existing-six-offerings:active-order-id:setOnInsert:strict-schema");

    public async Task UpAsync(IMongoDatabase database, CancellationToken ct)
    {
        var names = await (await database.ListCollectionNamesAsync(cancellationToken: ct)).ToListAsync(ct);
        if (!names.Contains("service_catalog"))
        {
            var properties = new BsonDocument();
            foreach (var field in new[] { "_id", "Title", "ImagePath", "Summary", "RateText", "Details", "BookingService", "Action", "ActionLabel" })
            {
                properties[field] = new BsonDocument { { "bsonType", "string" }, { "minLength", 1 } };
            }
            properties["BookingService"]["enum"] = new BsonArray { "Regular", "VIP", "PhilippineCoach", "KpgaPro", "KpgaGroup", "WholeShop" };
            properties["Action"]["enum"] = new BsonArray { "Book", "Contact" };
            properties["Active"] = new BsonDocument("bsonType", "bool");
            properties["Order"] = new BsonDocument("bsonType", "int");
            await database.CreateCollectionAsync("service_catalog", new CreateCollectionOptions<BsonDocument>
            {
                Validator = new BsonDocument("$jsonSchema", new BsonDocument
                {
                    { "bsonType", "object" }, { "required", new BsonArray(properties.Names) }, { "properties", properties }
                })
            }, ct);
        }

        var collection = database.GetCollection<ServiceCatalogRecord>("service_catalog");
        await collection.Indexes.CreateOneAsync(new CreateIndexModel<ServiceCatalogRecord>(
            Builders<ServiceCatalogRecord>.IndexKeys.Ascending(x => x.Active).Ascending(x => x.Order).Ascending(x => x.Id),
            new CreateIndexOptions { Name = "ix_service_catalog_active_order_id" }), cancellationToken: ct);
        // Seed only missing records so rerunning the migration preserves venue edits.
        foreach (var item in Seed())
        {
            await collection.UpdateOneAsync(x => x.Id == item.Id, new BsonDocument("$setOnInsert", item.ToBsonDocument()),
                new UpdateOptions { IsUpsert = true }, ct);
        }
    }

    private static IEnumerable<ServiceCatalogRecord> Seed()
    {
        (string Id, string Title, string Image, string Summary, string Rate, string Details, string Label)[] items =
        [
            ("Regular", "Regular simulator bay", "venue-overview", "Five regular bays. Play solo or select several bays for your group.", "₱600–₱800 / hour", "Mon–Fri: ₱600/hour before 3 PM; ₱800/hour from 3 PM–midnight. Weekends and holidays: ₱800/hour.", "Request a bay"),
            ("VIP", "VIP bay", "social-seating", "A little more room for your next round in VIP 1 or VIP 2.", "₱1,000 / hour", "VIP 1 and VIP 2. ₱1,000/hour during opening hours.", "Request a VIP bay"),
            ("PhilippineCoach", "Philippine coach", "coaching", "Make time for your game with a professional coaching session.", "₱2,000 / hour", "By reservation. Coach availability is confirmed by the venue.", "Request coaching"),
            ("KpgaPro", "KPGA pro coaching", "coaching-programs", "An eight-session coaching package. Start with your first visit.", "₱15,000 / 8 sessions", "Tuesday, Thursday and Saturday. First visit selected here; arrange the remaining sessions with the coach.", "Request your first session"),
            ("KpgaGroup", "KPGA lesson program", "kpga-lessons", "A monthly group lesson program for up to eight participants.", "₱2,500 / 4 classes", "4 classes per month. Tuesday, 3–4 PM. Up to 8 participants. First class selected here; arrange remaining classes with the venue.", "Request your first class"),
            ("WholeShop", "Rent the whole shop", "family-play", "Bring everyone together for a private event with exclusive use of the bays.", "₱40,000–₱80,000", "Mon–Thu: half day ₱40,000 / whole day ₱60,000. Fri–Sun and holidays: half day ₱50,000 / whole day ₱80,000. Half day: 3–9 PM; whole day: opening–midnight. A verified 50% deposit is required; contact the venue for pricing and payment arrangements.", "Contact the venue")
        ];
        return items.Select((x, i) => new ServiceCatalogRecord
        {
            Id = x.Id,
            Title = x.Title,
            ImagePath = $"/media/facebook/{x.Image}.webp",
            Summary = x.Summary,
            RateText = x.Rate,
            Details = x.Details,
            BookingService = x.Id,
            Action = x.Id == "WholeShop" ? "Contact" : "Book",
            ActionLabel = x.Label,
            Order = i,
            Active = true
        });
    }
}
