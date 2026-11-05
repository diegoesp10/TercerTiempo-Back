using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Licensing;

namespace TercerTiempo.Hosting;

public enum LicenseScope
{
    Squad,
    Video,
    Player,

    // Rutas del club sobre uno de sus equipos (/teams/{teamId}/...): fichas, fotos y escudo.
    Team,
}

// Puerta de licencias: antes de ejecutar la operación comprueba que el equipo al que pertenece el recurso (plantilla,
// vídeo, panel del jugador) tiene licencia vigente, y que quien mira datos de otro equipo pertenece a uno con
// licencia. Los permisos de cada acción los siguen decidiendo los handlers.
internal sealed class LicenseEndpointFilter(LicenseScope scope) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var gate = http.RequestServices.GetRequiredService<ILicenseGate>();
        var user = http.RequestServices.GetRequiredService<ICurrentUser>();
        var ct = http.RequestAborted;

        switch (scope)
        {
            case LicenseScope.Squad when http.Request.RouteValues.TryGetValue("squadId", out var squad) && Guid.TryParse(squad?.ToString(), out var squadId):
                await gate.EnsureSquadAccessAsync(squadId, user, ct);
                break;
            // El reproductor y la descarga no pueden mandar el JWT: van con un ticket, que solo se emite tras comprobar el
            // acceso (también la licencia) en POST /ticket o /download-ticket. Pedirles aquí la sesión los rechazaba siempre.
            case LicenseScope.Video when http.Request.RouteValues.TryGetValue("videoId", out var video) && Guid.TryParse(video?.ToString(), out var videoId)
                && !(HttpMethods.IsGet(http.Request.Method) && IsTicketRoute(http.Request.Path.Value)):
                await gate.EnsureVideoAccessAsync(videoId, user, ct);
                break;
            case LicenseScope.Team when http.Request.RouteValues.TryGetValue("teamId", out var team) && Guid.TryParse(team?.ToString(), out var teamId):
                await gate.EnsureTeamLicensedAsync(teamId, ct);
                break;
            case LicenseScope.Player:
                await gate.EnsurePlayerAccessAsync(user, ct);
                break;
        }

        return await next(context);
    }

    private static bool IsTicketRoute(string? path) =>
        path is not null && (path.EndsWith("/stream", StringComparison.Ordinal) || path.EndsWith("/download", StringComparison.Ordinal));
}

public static class LicenseFilterExtensions
{
    public static TBuilder RequireLicense<TBuilder>(this TBuilder builder, LicenseScope scope) where TBuilder : IEndpointConventionBuilder =>
        builder.AddEndpointFilter(new LicenseEndpointFilter(scope));
}
