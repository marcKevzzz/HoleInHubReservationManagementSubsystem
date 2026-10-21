using HoleInHub.Data.Configuration;
using HoleInHub.Data.Context;
using HoleInHub.Data.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HoleInHub.Data.Extensions;


public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddHoleInHubData(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<MongoDbOptions>(
            configuration.GetSection(MongoDbOptions.SectionName));

        services.AddSingleton<IMongoDbContext, MongoDbContext>();

        return services;
    }

    public static IServiceCollection AddHoleInHubMigrations(this IServiceCollection services)
    {
        services.AddSingleton<MongoMigrationLeaseManager>();
        services.AddSingleton<IMongoMigration, Migrations.Definitions.Migration_001_InitialFoundation>();
        services.AddSingleton<MongoMigrationRunner>();

        return services;
    }
}

