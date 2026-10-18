using HoleInHub.Data.Extensions;
using HoleInHub.Data.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true)
    .AddEnvironmentVariables()
    .AddCommandLine(args)
    .Build();

var services = new ServiceCollection();

services.AddLogging(builder =>
{
    builder.AddConfiguration(configuration.GetSection("Logging"));
    builder.AddConsole();
});

services.AddHoleInHubData(configuration);

using var serviceProvider = services.BuildServiceProvider();
var logger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("HoleInHub.Migrator");

logger.LogInformation("==================================================");
logger.LogInformation(" Hole In Hub Database Migration Tool Starting");
logger.LogInformation("==================================================");

try
{
    var runner = serviceProvider.GetRequiredService<MongoMigrationRunner>();
    var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        cts.Cancel();
        logger.LogWarning("Cancellation requested by operator. Stopping gracefully...");
    };

    var result = await runner.RunAsync(ct: cts.Token);

    logger.LogInformation("==================================================");
    logger.LogInformation(" Migration execution completed successfully.");
    logger.LogInformation(" Total Migrations:  {Total}", result.TotalDiscovered);
    logger.LogInformation(" Already Applied:   {Applied}", result.AlreadyApplied);
    logger.LogInformation(" Newly Applied:     {NewlyApplied}", result.NewlyApplied);
    logger.LogInformation(" Elapsed Time:      {ElapsedMs} ms", result.Elapsed.TotalMilliseconds);
    logger.LogInformation("==================================================");

    return 0;
}
catch (MigrationChecksumMismatchException ex)
{
    logger.LogCritical("CRITICAL CHECKSUM ERROR: {Message}", ex.Message);
    return 1;
}
catch (MigrationLeaseException ex)
{
    logger.LogError("LEASE CONFLICT: {Message}", ex.Message);
    return 1;
}
catch (Exception ex)
{
    logger.LogCritical(ex, "MIGRATION RUN FAILED: {Message}", ex.Message);
    return 1;
}
