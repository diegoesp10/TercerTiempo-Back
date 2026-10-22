using FluentValidation;
using TercerTiempo.Application.Abstractions.Errors;
using TercerTiempo.Application.Sync;
using Microsoft.Extensions.DependencyInjection;
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
        services.AddScoped<AppointmentRedemption>();
        services.AddScoped<OfficialSheetLocator>();

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
