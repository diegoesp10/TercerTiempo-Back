using Microsoft.Extensions.DependencyInjection.Extensions;
using TercerTiempo.Api.Infrastructure;
using TercerTiempo.Hosting;

namespace TercerTiempo.Api.Extensions;

public static class ApiServiceExtensions
{
    public static IServiceCollection AddApi(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddWebHost(configuration);
        services.Replace(ServiceDescriptor.Scoped<TercerTiempo.Application.Seasons.ISeasonRequest, SeasonRequest>());
        services.AddOpenApiDocs(configuration, ApiDocs.Info);

        return services;
    }
}
