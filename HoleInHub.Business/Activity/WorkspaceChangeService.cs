using HoleInHub.Data.Activity;

namespace HoleInHub.Business.Activity;

public sealed class WorkspaceChangeService(WorkspaceChangePersistence persistence)
{
    public Task WatchAsync(Func<CancellationToken, Task> changed, CancellationToken ct) => persistence.WatchAsync(changed, ct);
}
