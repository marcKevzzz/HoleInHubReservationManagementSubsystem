using HoleInHub.Data.Context;
using HoleInHub.Data.Entities;
using MongoDB.Driver;

namespace HoleInHub.Business.Services;

public sealed class HealthService(IMongoDbContext dbContext) : IHealthService
{
    private readonly IMongoDbContext _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));

    public async Task<bool> CheckDatabaseLivenessAsync(CancellationToken ct = default)
    {
        return await _dbContext.PingAsync(ct);
    }

    public async Task<(bool IsReady, string StatusDescription)> CheckDatabaseReadinessAsync(CancellationToken ct = default)
    {
        var pingSuccess = await _dbContext.PingAsync(ct);
        if (!pingSuccess)
        {
            return (false, "MongoDB is unreachable.");
        }

        try
        {
            var count = await _dbContext.Migrations.CountDocumentsAsync(
                FilterDefinition<MigrationRecord>.Empty,
                cancellationToken: ct);

            if (count > 0)
            {
                return (true, $"MongoDB online and {count} migrations applied.");
            }

            return (false, "MongoDB online but required baseline migrations have not been applied.");
        }
        catch (Exception ex)
        {
            return (false, $"Error verifying migrations readiness: {ex.Message}");
        }
    }
}
