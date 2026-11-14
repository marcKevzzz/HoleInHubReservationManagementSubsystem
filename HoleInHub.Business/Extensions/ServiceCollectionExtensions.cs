using HoleInHub.Business.Services;
using HoleInHub.Data.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HoleInHub.Business.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddHoleInHubBusiness(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddHoleInHubData(configuration);
        services.AddScoped<IHealthService, HealthService>();
        services.AddScoped<HoleInHub.Business.Activity.ActivityService>();
        services.AddScoped<HoleInHub.Business.Activity.WorkspaceChangeService>();
        services.AddScoped<HoleInHub.Business.Customers.CustomerProfileService>();
        services.AddScoped<HoleInHub.Business.Settings.OperationalSettings>();
        services.AddScoped<ServiceCatalog>();
        services.AddScoped<IServiceCatalog>(provider => provider.GetRequiredService<ServiceCatalog>());
        services.AddSingleton(TimeProvider.System);
        services.Configure<HoleInHub.Business.Reservations.ReservationPolicy>(configuration.GetSection("ReservationPolicy"));
        services.AddScoped<HoleInHub.Business.Reservations.ReservationService>();
        services.AddScoped<HoleInHub.Business.Reservations.GuestBookingService>();
        services.AddScoped<HoleInHub.Business.Accounts.EmailVerificationService>();
        services.AddScoped<HoleInHub.Business.Accounts.NotificationDeliveryService>();

        return services;
    }
}
