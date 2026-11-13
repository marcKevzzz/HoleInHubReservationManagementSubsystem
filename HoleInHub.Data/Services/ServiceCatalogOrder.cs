using MongoDB.Driver;
namespace HoleInHub.Data.Services;
public sealed partial class ServiceCatalogPersistence
{
    public async Task ReorderAsync(IReadOnlyList<ServiceCatalogRecord> rows, string actor, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        using var session = await context.Client.StartSessionAsync(cancellationToken: timeout.Token);
        await session.WithTransactionAsync(async (s, token) =>
        {
            var current = await Catalog.Find(s, x => !x.Deleted).SortBy(x => x.Order).ThenBy(x => x.Id).ToListAsync(token);
            if (current.Count != rows.Count || current.Any(record => !rows.Any(row => row.Id == record.Id)))
            {
                throw new InvalidOperationException("The service list changed. Reload before reordering.");
            }
            if (current.Select(record => record.Id).SequenceEqual(rows.Select(row => row.Id)))
            {
                return true;
            }
            foreach (var row in rows)
            {
                if (current.First(record => record.Id == row.Id).Revision != row.Revision)
                {
                    throw new InvalidOperationException("A service changed. Reload before reordering.");
                }
            }
            for (var index = 0; index < rows.Count; index++)
            {
                var row = rows[index];
                var result = await Catalog.UpdateOneAsync(s, x => x.Id == row.Id && x.Revision == row.Revision,
                    Builders<ServiceCatalogRecord>.Update.Set(x => x.Order, index).Inc(x => x.Revision, 1)
                        .Set(x => x.UpdatedBy, actor).Set(x => x.UpdatedUtc, DateTime.UtcNow), cancellationToken: token);
                if (result.ModifiedCount != 1)
                {
                    throw new InvalidOperationException("A service changed. Reload before reordering.");
                }
            }
            return true;
        }, new TransactionOptions(readConcern: ReadConcern.Snapshot, writeConcern: WriteConcern.WMajority), timeout.Token);
    }
}
