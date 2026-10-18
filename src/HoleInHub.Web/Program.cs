using HoleInHub.Business.Extensions;
using HoleInHub.Business.Services;

var builder = WebApplication.CreateBuilder(args);

// Register business and data services
builder.Services.AddHoleInHubBusiness(builder.Configuration);

// Add services to the container.
builder.Services.AddControllersWithViews();
builder.Services.AddHealthChecks();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();

// Correlation identifier middleware
app.Use(async (context, next) =>
{
    const string correlationHeader = "X-Correlation-ID";
    if (!context.Request.Headers.TryGetValue(correlationHeader, out var correlationId) || string.IsNullOrWhiteSpace(correlationId))
    {
        correlationId = Guid.NewGuid().ToString("N");
    }
    context.TraceIdentifier = correlationId!;
    context.Response.Headers[correlationHeader] = correlationId;
    await next();
});

app.UseRouting();

app.UseAuthorization();

// Liveness probe - returns 200 if process is listening
app.MapGet("/health/live", () => Results.Ok(new
{
    status = "Healthy",
    timestampUtc = DateTime.UtcNow
}));

// Readiness probe - checks MongoDB ping and baseline migrations
app.MapGet("/health/ready", async (IHealthService healthService, CancellationToken ct) =>
{
    var (isReady, description) = await healthService.CheckDatabaseReadinessAsync(ct);
    if (!isReady)
    {
        return Results.Json(new
        {
            status = "Unhealthy",
            description,
            timestampUtc = DateTime.UtcNow
        }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    return Results.Ok(new
    {
        status = "Healthy",
        description,
        timestampUtc = DateTime.UtcNow
    });
});

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();

public partial class Program { }
