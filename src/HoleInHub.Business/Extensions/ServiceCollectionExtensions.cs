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

        return services;
    }
}
