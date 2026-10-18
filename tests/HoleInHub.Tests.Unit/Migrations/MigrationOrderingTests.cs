using FluentAssertions;
using HoleInHub.Data.Migrations;
using HoleInHub.Data.Migrations.Definitions;
using MongoDB.Driver;
using Xunit;

namespace HoleInHub.Tests.Unit.Migrations;

public sealed class MigrationOrderingTests
{
    private sealed class StubMigration(string id) : IMongoMigration
    {
        public string MigrationId => id;
        public string Description => $"Stub {id}";
        public string CalculateChecksum() => "checksum";
        public Task UpAsync(IMongoDatabase database, CancellationToken ct) => Task.CompletedTask;
    }

    [Fact]
    public void Migrations_ShouldBeOrderedLexicographicallyById()
    {
        // Arrange
        var unordered = new List<IMongoMigration>
        {
            new StubMigration("003_AddIndices"),
            new StubMigration("001_InitialFoundation"),
            new StubMigration("002_AddCustomers")
        };

        // Act
        var ordered = unordered.OrderBy(m => m.MigrationId, StringComparer.Ordinal).ToList();

        // Assert
        ordered.Select(m => m.MigrationId).Should().ContainInOrder(
            "001_InitialFoundation",
            "002_AddCustomers",
            "003_AddIndices");
    }

    [Fact]
    public void BaselineMigration_ShouldHaveValidIdAndChecksum()
    {
        // Arrange
        var migration = new Migration_001_InitialFoundation();

        // Act & Assert
        migration.MigrationId.Should().Be("001_InitialFoundation");
        migration.Description.Should().NotBeNullOrWhiteSpace();
        migration.CalculateChecksum().Should().HaveLength(64);
    }
}
