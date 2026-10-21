using FluentAssertions;
using HoleInHub.Business.Extensions;
using HoleInHub.Data.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HoleInHub.Tests.Unit.Migrations;

public sealed class MigrationRegistrationTests
{
    [Fact]
    public void WebBusinessRegistration_DoesNotIncludeMigrationExecutionServices()
    {
        var services = new ServiceCollection();
        services.AddHoleInHubBusiness(new ConfigurationBuilder().Build());

        services.Should().NotContain(descriptor =>
            descriptor.ServiceType == typeof(MongoMigrationRunner) ||
            descriptor.ServiceType == typeof(MongoMigrationLeaseManager) ||
            descriptor.ServiceType == typeof(IMongoMigration));
    }
}
