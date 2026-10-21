namespace HoleInHub.Business.Services;

public interface IHealthService
{
    Task<bool> CheckDatabaseLivenessAsync(CancellationToken ct = default);
    Task<(bool IsReady, string StatusDescription)> CheckDatabaseReadinessAsync(CancellationToken ct = default);
}
