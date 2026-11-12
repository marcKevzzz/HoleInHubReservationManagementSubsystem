using HoleInHub.Data.Services;
namespace HoleInHub.Business.Services;
public sealed record ServicePosition(string Id, long Revision);
public sealed partial class ServiceCatalog
{
    public static void ValidateOrder(IReadOnlyList<ServicePosition> rows)
    {
        if (rows.Count is < 1 or > 1000 || rows.Any(row => row is null || string.IsNullOrWhiteSpace(row.Id) || row.Revision < 0)
            || rows.Select(row => row.Id).Distinct().Count() != rows.Count)
        {
            throw new ArgumentException("Reload the services before reordering.");
        }
    }
    public async Task ReorderAsync(IReadOnlyList<ServicePosition> rows, string actor, CancellationToken ct)
    {
        ValidateOrder(rows);
        if (string.IsNullOrWhiteSpace(actor))
        {
            throw new ArgumentException("Sign in before reordering services.");
        }
        await persistence.ReorderAsync(rows.Select(row => new ServiceCatalogRecord { Id = row.Id, Revision = row.Revision }).ToArray(), actor, ct);
    }
}
