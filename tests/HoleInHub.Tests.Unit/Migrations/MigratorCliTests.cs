using System.Diagnostics;
using FluentAssertions;
using Xunit;

namespace HoleInHub.Tests.Unit.Migrations;

public sealed class MigratorCliTests
{
    [Theory]
    [InlineData("", 0)]
    [InlineData("--help", 0)]
    [InlineData("-h", 0)]
    [InlineData("unknown", 2)]
    [InlineData("migrate --help", 2)]
    public async Task NonMigrationCommands_ShowUsageWithoutConnecting(string arguments, int expectedExitCode)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        var configuration = directory.Parent!.Name;
        while (!File.Exists(Path.Combine(directory.FullName, "HoleInHubReservationManagementSubsystem.slnx")))
        {
            directory = directory.Parent ?? throw new InvalidOperationException("Solution root not found.");
        }

        var assembly = Path.Combine(directory.FullName, "HoleInHub.Migrator", "bin", configuration,
            "net10.0", "HoleInHub.Migrator.dll");
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add(assembly);
        foreach (var argument in arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            start.ArgumentList.Add(argument);
        }
        // A help/invalid command must work even when database configuration is invalid.
        start.Environment["MongoDb__ConnectionString"] = "invalid-uri";
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }

        process.ExitCode.Should().Be(expectedExitCode);
        (await output + await error).Should().Contain("Usage:").And.NotContain("Tool Starting");
    }
}
