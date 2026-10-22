using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Licensing;

namespace TercerTiempo.Api.Infrastructure;

internal enum LicenseScope
{
    Squad,
    Video,
    SharedVideo,
    Player,
}

// Puerta de licencias en la API: antes de ejecutar la operación comprueba que el equipo al que pertenece el recurso
// (plantilla, vídeo, panel del jugador) tiene licencia vigente, y que quien mira datos de otro equipo pertenece a
// uno con licencia. Los permisos de cada acción los siguen decidiendo los handlers.
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
            case LicenseScope.Video when http.Request.RouteValues.TryGetValue("videoId", out var video) && Guid.TryParse(video?.ToString(), out var videoId):
                await gate.EnsureVideoAccessAsync(videoId, user, ct);
                break;
            case LicenseScope.SharedVideo when http.Request.RouteValues.TryGetValue("token", out var token) && token is string t:
                await gate.EnsureSharedVideoAccessAsync(t, ct);
                break;
            case LicenseScope.Player:
                await gate.EnsurePlayerAccessAsync(user, ct);
                break;
        }

        return await next(context);
    }
}

internal static class LicenseFilterExtensions
{
    public static TBuilder RequireLicense<TBuilder>(this TBuilder builder, LicenseScope scope) where TBuilder : IEndpointConventionBuilder =>
        builder.AddEndpointFilter(new LicenseEndpointFilter(scope));
}
