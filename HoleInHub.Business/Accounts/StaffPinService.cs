using HoleInHub.Data.Accounts;

namespace HoleInHub.Business.Accounts;

public sealed record StaffPinSecret(string Hash, string Version);

public sealed class StaffPinService(StaffPinPersistence persistence, TimeProvider clock)
{
    public Task<bool> TakeDeviceAttemptAsync(string device, CancellationToken ct) =>
        persistence.TakeAttemptAsync("desktop:" + device, clock.GetUtcNow().UtcDateTime, 20, ct);

    public Task<bool> TakeEmployeeAttemptAsync(string accountId, CancellationToken ct) =>
        persistence.TakeAttemptAsync("employee:" + accountId, clock.GetUtcNow().UtcDateTime, 5, ct);

    public async Task<StaffPinSecret?> ReadAsync(string accountId, CancellationToken ct)
    {
        var saved = await persistence.ReadAsync(accountId, ct);
        return saved is null ? null : new(saved.Hash, saved.Version);
    }

    public Task<bool> TrySaveAsync(string accountId, string hash, string expectedStamp, CancellationToken ct) =>
        persistence.TrySaveAsync(new StaffPinCredential(accountId, hash, Guid.NewGuid().ToString("N")),
            expectedStamp, StaffRoles.Workspace, ct);
}
