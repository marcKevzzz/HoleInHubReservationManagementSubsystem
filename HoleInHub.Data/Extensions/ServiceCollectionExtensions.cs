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
        services.AddScoped<HoleInHub.Data.Activity.WorkspaceChangePersistence>();
        services.AddScoped<HoleInHub.Data.Accounts.AccountPersistence>();
        services.AddScoped<HoleInHub.Data.Activity.ActivityPersistence>();
        services.AddScoped<HoleInHub.Data.Customers.CustomerProfilePersistence>();
        services.AddScoped<HoleInHub.Data.Accounts.StaffPinPersistence>();
        services.AddScoped<HoleInHub.Data.Services.ServiceCatalogPersistence>();
        services.AddScoped<HoleInHub.Data.Settings.SystemSettingsPersistence>();
        services.AddScoped<HoleInHub.Data.Reservations.ReservationPersistence>();
        services.AddScoped<HoleInHub.Data.Reservations.EmailVerificationPersistence>();
        services.AddScoped<HoleInHub.Data.Reservations.OutboxPersistence>();

        return services;
    }

    public static IServiceCollection AddHoleInHubMigrations(this IServiceCollection services)
    {
        services.AddSingleton<MongoMigrationLeaseManager>();
        services.AddSingleton<IMongoMigration, Migrations.Definitions.Migration_001_InitialFoundation>();
        services.AddSingleton<IMongoMigration, Migrations.Definitions.Migration_002_Reservations>();
        services.AddSingleton<IMongoMigration, Migrations.Definitions.AccountsMigration>();
        services.AddSingleton<IMongoMigration, Migrations.Definitions.Migration_004_BookingProcess>();
        services.AddSingleton<IMongoMigration, Migrations.Definitions.Migration_005_GoogleAccounts>();
        services.AddSingleton<IMongoMigration, Migrations.Definitions.Migration_006_SchemaHardening>();
        services.AddSingleton<IMongoMigration, Migrations.Definitions.Migration_007_GuestReservationAccess>();
        services.AddSingleton<IMongoMigration, Migrations.Definitions.Migration_008_ServiceCatalog>();
        services.AddSingleton<IMongoMigration, Migrations.Definitions.Migration_009_RecoveryQuotaValidation>();
        services.AddSingleton<IMongoMigration, Migrations.Definitions.Migration_010_StaffAssistedReservations>();
        services.AddSingleton<IMongoMigration, Migrations.Definitions.Migration_011_AdminScheduleBlocks>();
        services.AddSingleton<IMongoMigration, Migrations.Definitions.Migration_012_ReleaseScheduleBlocks>();
        services.AddSingleton<IMongoMigration, Migrations.Definitions.Migration_013_ReservationAttendance>();
        services.AddSingleton<IMongoMigration, Migrations.Definitions.Migration_014_OperatingReviewClock>();
        services.AddSingleton<IMongoMigration, Migrations.Definitions.Migration_015_ScheduleChanges>();
        services.AddSingleton<IMongoMigration, Migrations.Definitions.Migration_016_StaffPinAttempts>();
        services.AddSingleton<IMongoMigration, Migrations.Definitions.Migration_017_StaffRoleAdministration>();
        services.AddSingleton<IMongoMigration, Migrations.Definitions.Migration_018_CustomerProfiles>();
        services.AddSingleton<IMongoMigration, Migrations.Definitions.Migration_019_SystemSettings>();
        services.AddSingleton<MongoMigrationRunner>();

        return services;
    }
}
