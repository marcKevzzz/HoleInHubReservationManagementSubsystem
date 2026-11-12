using HoleInHub.Data.Context;
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
        IReadOnlyList<ServiceImageRecord> images, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        using var session = await context.Client.StartSessionAsync(cancellationToken: timeout.Token);
        await session.WithTransactionAsync(async (s, token) =>
        {
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
