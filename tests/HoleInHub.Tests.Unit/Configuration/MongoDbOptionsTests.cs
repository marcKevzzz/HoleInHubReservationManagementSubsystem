using FluentAssertions;
using HoleInHub.Data.Configuration;
using Xunit;

namespace HoleInHub.Tests.Unit.Configuration;

public sealed class MongoDbOptionsTests
{
    [Fact]
    public void Validate_WithValidOptions_DoesNotThrow()
    {
        // Arrange
        var options = new MongoDbOptions
        {
            ConnectionString = "mongodb://localhost:27017",
            DatabaseName = "holeinhub_test"
        };

        // Act
        var act = () => options.Validate();

        // Assert
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("", "db_name")]
    [InlineData("   ", "db_name")]
    [InlineData(null, "db_name")]
    public void Validate_WithEmptyConnectionString_ThrowsInvalidOperationException(string? conn, string db)
    {
        // Arrange
        var options = new MongoDbOptions
        {
            ConnectionString = conn!,
            DatabaseName = db
        };

        // Act
        var act = () => options.Validate();

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*ConnectionString*");
    }

    [Theory]
    [InlineData("mongodb://localhost:27017", "")]
    [InlineData("mongodb://localhost:27017", "   ")]
    [InlineData("mongodb://localhost:27017", null)]
    public void Validate_WithEmptyDatabaseName_ThrowsInvalidOperationException(string conn, string? db)
    {
        // Arrange
        var options = new MongoDbOptions
        {
            ConnectionString = conn,
            DatabaseName = db!
        };

        // Act
        var act = () => options.Validate();

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*DatabaseName*");
    }
}
