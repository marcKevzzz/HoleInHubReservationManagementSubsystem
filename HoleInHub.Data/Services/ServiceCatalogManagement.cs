using HoleInHub.Data.Context;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
namespace HoleInHub.Data.Services;

public sealed class ServiceImageRecord
{
    [BsonId] public string Id { get; set; } = "";
    public string ContentType { get; set; } = "";
    public byte[] Content { get; set; } = [];
}
public sealed partial class ServiceCatalogPersistence
{
    private IMongoCollection<ServiceCatalogRecord> Catalog => context.Database.GetCollection<ServiceCatalogRecord>("service_catalog");
    public async Task<IReadOnlyList<ServiceCatalogRecord>> GetAllAsync(CancellationToken ct) =>
        await Catalog.Find(x => !x.Deleted).SortBy(x => x.Order).ThenBy(x => x.Id).ToListAsync(ct);
    public Task<ServiceCatalogRecord?> GetAsync(string id, CancellationToken ct) =>
        Catalog.Find(x => x.Id == id && !x.Deleted).FirstOrDefaultAsync(ct)!;
    public Task<ServiceImageRecord?> ImageAsync(string id, CancellationToken ct) =>
        context.Database.GetCollection<ServiceImageRecord>("service_images").Find(x => x.Id == id).FirstOrDefaultAsync(ct)!;

    public async Task SaveAsync(ServiceCatalogRecord record, long revision, bool create,
        IReadOnlyList<ServiceImageRecord> images, CancellationToken ct,
        Action<IReadOnlyList<HoleInHub.Data.Reservations.ReservationRecord>>? checkImpacts = null)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        using var session = await context.Client.StartSessionAsync(cancellationToken: timeout.Token);
        await session.WithTransactionAsync(async (s, token) =>
        {
            await context.Database.GetCollection<BsonDocument>("allocation_guards").UpdateOneAsync(s,
                new BsonDocument("_id", "catalog"), new BsonDocument("$inc", new BsonDocument("version", 1)),
                new UpdateOptions { IsUpsert = true }, token);
            var prior = await Catalog.Find(s, x => x.Id == record.Id).FirstOrDefaultAsync(token);
            if (prior?.CommandId == record.CommandId)
            {
                if (prior.CommandHash != record.CommandHash)
                {
                    throw new InvalidOperationException("This action was already saved with different details. Reload the service.");
                }

                return true;
            }
            if (create ? prior is not null : prior is null || prior.Deleted || prior.Revision != revision)
            {
                throw new InvalidOperationException("This service changed. Reload its latest details before saving.");
            }

            if (prior is not null && checkImpacts is not null)
            {
                var now = record.UpdatedUtc ?? DateTime.UtcNow;
                var bookings = await context.Database.GetCollection<HoleInHub.Data.Reservations.ReservationRecord>("reservations")
                    .Find(s, booking => (booking.ServiceEntryId == prior.Id ||
                        booking.ServiceEntryId == null && prior.Id == booking.Service) &&
                        booking.StartUtc > now && (booking.Status == "Pending" || booking.Status == "Confirmed"))
                    .ToListAsync(token);
                checkImpacts(bookings);
            }

            foreach (var image in images)
            {
                await context.Database.GetCollection<ServiceImageRecord>("service_images")
                    .ReplaceOneAsync(s, x => x.Id == image.Id, image, new ReplaceOptions { IsUpsert = true }, token);
            }
            record.Revision = (prior?.Revision ?? -1) + 1;
            if (create)
            {
                await Catalog.InsertOneAsync(s, record, cancellationToken: token);
            }
            else
            {
                await Catalog.ReplaceOneAsync(s, x => x.Id == record.Id, record, cancellationToken: token);
            }

            return true;
        }, new TransactionOptions(readConcern: ReadConcern.Snapshot, writeConcern: WriteConcern.WMajority), timeout.Token);
    }
}
