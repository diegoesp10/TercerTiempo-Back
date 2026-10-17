using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using TercerTiempo.Application.Abstractions.Messaging;

namespace TercerTiempo.Application;

public static class DependencyInjection
{
    /// <summary>
    /// Registra validadores y todos los handlers de la capa por reflexión.
    /// Los command handlers se envuelven en <see cref="ValidatingCommandHandler{TCommand,TResult}"/>.
    /// </summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var assembly = typeof(DependencyInjection).Assembly;

        services.AddValidatorsFromAssembly(assembly, includeInternalTypes: true);

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
