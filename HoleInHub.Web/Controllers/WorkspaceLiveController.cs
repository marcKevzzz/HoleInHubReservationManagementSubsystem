using HoleInHub.Web.Accounts;
using HoleInHub.Web.Live;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;

namespace HoleInHubReservationManagementSubsystem.Controllers;

[Authorize(Policy = StaffAuthorization.Workspace)]
public sealed class WorkspaceLiveController(WorkspaceUpdates updates) : Controller
{
    [HttpGet("/staff/live")]
    public async Task Stream(CancellationToken ct)
    {
        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-store";
        Response.Headers["X-Accel-Buffering"] = "no";
        HttpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();
        var subscription = updates.Subscribe();
        using var heartbeat = new PeriodicTimer(TimeSpan.FromSeconds(15));
        try
        {
            await Response.WriteAsync("retry: 1000\nevent: change\ndata: {}\n\n", ct);
            await Response.Body.FlushAsync(ct);
            var read = subscription.Reader.WaitToReadAsync(ct).AsTask();
            var tick = heartbeat.WaitForNextTickAsync(ct).AsTask();
            while (!ct.IsCancellationRequested)
            {
                await Task.WhenAny(read, tick);
                if (read.IsCompleted)
                {
                    if (!await read) { break; }
                    while (subscription.Reader.TryRead(out _)) { }
                    await Response.WriteAsync("event: change\ndata: {}\n\n", ct);
                    read = subscription.Reader.WaitToReadAsync(ct).AsTask();
                }
                if (tick.IsCompleted)
                {
                    if (!await tick) { break; }
                    await Response.WriteAsync(": heartbeat\n\n", ct);
                    tick = heartbeat.WaitForNextTickAsync(ct).AsTask();
                }
                await Response.Body.FlushAsync(ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        finally { updates.Unsubscribe(subscription.Id); }
    }
}
