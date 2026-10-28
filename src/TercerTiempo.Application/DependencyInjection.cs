using FluentValidation;
using TercerTiempo.Application.Abstractions.Errors;
using TercerTiempo.Application.Sync;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Auth;
using TercerTiempo.Application.Coaches.Appointments;
using TercerTiempo.Application.OfficialSheets;

namespace TercerTiempo.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var assembly = typeof(DependencyInjection).Assembly;

        services.AddValidatorsFromAssembly(assembly, includeInternalTypes: true);

        // Servicios internos compartidos por varios handlers.
        services.AddScoped<AccountStores>();
        services.AddScoped<Clubs.Roster.RosterStores>();
        services.AddScoped<IErrorMessageResolver, ErrorMessageResolver>();
        services.AddSingleton<ISyncProgress, SyncProgress>();
        services.AddSingleton<Jobs.IJobProgress, Jobs.JobProgress>();
        services.AddSingleton<Presence.PresenceBoard>();
        services.AddSingleton<Search.SearchIndexHolder>();
        services.AddScoped<AppointmentRedemption>();
        services.AddScoped<Licensing.ILicenseGate, Licensing.LicenseGate>();
        services.AddSingleton<Licensing.LicensingCatalogCache>();
        services.AddScoped<Admin.FootballSyncSettingsReader>();
        services.AddScoped<Seasons.ISeasonContext, Seasons.SeasonContext>();
        services.TryAddScoped<Seasons.ISeasonRequest, Seasons.NoSeasonRequest>();
        services.AddScoped<Licensing.ILicensingCatalog, Licensing.LicensingCatalog>();
        services.AddScoped<Licensing.LicenseDescriber>();
        services.AddScoped<Licensing.LicenseCheckoutCompletion>();
        services.AddScoped<Alerts.LicenseAlertSync>();
        services.AddScoped<Referees.MatchOfficialsWriter>();
        services.AddScoped<Moderation.CommentModeration>();
        services.AddScoped<Stats.Predictions.ForecastSources>();
        services.AddSingleton<Stats.Predictions.LeagueForecastCache>();
        services.AddScoped<OfficialSheetLocator>();
        services.AddSingleton<OfficialSheets.OfficialSheetSyncMemory>();
        services.AddScoped<IPublicSheetSource, FederationSheetSource>();

        var handlerTypes = assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false });

        foreach (var handlerType in handlerTypes)
        {
            foreach (var contract in handlerType.GetInterfaces().Where(i => i.IsGenericType))
            {
                var definition = contract.GetGenericTypeDefinition();

                if (definition == typeof(ICommandHandler<,>))
                {
                    var concrete = handlerType;
                    var decorator = typeof(ValidatingCommandHandler<,>).MakeGenericType(contract.GetGenericArguments());

                    services.AddScoped(concrete);
                    services.AddScoped(contract, provider =>
                        ActivatorUtilities.CreateInstance(provider, decorator, provider.GetRequiredService(concrete)));
                }
                else if (definition == typeof(IQueryHandler<,>))
                {
                    services.AddScoped(contract, handlerType);
                }
            }
        }

        return services;
    }
}
