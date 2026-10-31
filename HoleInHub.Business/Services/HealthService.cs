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
            string[] required = ["001_InitialFoundation", "002_Reservations", "003_Accounts"];
            var applied = await _dbContext.Migrations.Find(Builders<MigrationRecord>.Filter.In(x => x.MigrationId, required)).Project(x => x.MigrationId).ToListAsync(ct);
            if (required.All(applied.Contains))
            {
                return (true, "MongoDB online and all required migrations applied.");
            }
            return (false, "MongoDB online but required baseline migrations have not been applied.");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            return (false, "Error verifying database readiness.");
        }
    }
}
