using System.Collections.Concurrent;
using System.Threading.Channels;
using HoleInHub.Business.Activity;

namespace HoleInHub.Web.Live;

// Only invalidation signals leave this broker; clients re-read their authorized views.
public sealed class WorkspaceUpdates
{
    private readonly ConcurrentDictionary<Guid, Channel<bool>> subscribers = new();
    public bool HasSubscribers => !subscribers.IsEmpty;
    public (Guid Id, ChannelReader<bool> Reader) Subscribe()
    {
        var channel = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });
        var id = Guid.NewGuid(); subscribers[id] = channel;
        return (id, channel.Reader);
    }
    public void Unsubscribe(Guid id)
    {
        if (subscribers.TryRemove(id, out var channel)) { channel.Writer.TryComplete(); }
    }
    public void Publish()
    {
        foreach (var channel in subscribers.Values) { channel.Writer.TryWrite(true); }
    }
}

public sealed class WorkspaceChangeWorker(IServiceScopeFactory scopes, WorkspaceUpdates updates, ILogger<WorkspaceChangeWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var reported = false;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!updates.HasSubscribers) { await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken); continue; }
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<WorkspaceChangeService>().WatchAsync(_ =>
                {
                    reported = false; updates.Publish(); return Task.CompletedTask;
                }, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception error)
            {
                if (!reported) { logger.LogWarning("Workspace change stream unavailable ({FailureType}); automatic polling remains available.", error.GetType().Name); reported = true; }
                try { await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            }
        }
    }
}
