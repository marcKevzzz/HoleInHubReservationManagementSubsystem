using FluentAssertions;
using HoleInHub.Data.Migrations;
using Xunit;

namespace HoleInHub.Tests.Unit.Migrations;

public sealed class MigrationChecksumTests
{
    [Fact]
    public void Compute_WithIdenticalContent_ReturnsIdenticalChecksum()
    {
        // Arrange
        const string content = "migration_content_alpha_v1";

        // Act
        var hash1 = MongoMigrationChecksum.Compute(content);
        var hash2 = MongoMigrationChecksum.Compute(content);

        // Assert
        hash1.Should().Be(hash2);
        hash1.Should().HaveLength(64);
    }

    [Fact]
    public void Compute_WithModifiedContent_ReturnsDifferentChecksum()
    {
        // Arrange
        const string originalContent = "migration_content_v1";
        const string modifiedContent = "migration_content_v2";

        // Act
        var originalHash = MongoMigrationChecksum.Compute(originalContent);
        var modifiedHash = MongoMigrationChecksum.Compute(modifiedContent);

        // Assert
        originalHash.Should().NotBe(modifiedHash);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Compute_WithNullOrWhitespace_ThrowsArgumentException(string invalidContent)
    {
        // Act
        var act = () => MongoMigrationChecksum.Compute(invalidContent);

        // Assert
        act.Should().Throw<ArgumentException>();
    }
}
