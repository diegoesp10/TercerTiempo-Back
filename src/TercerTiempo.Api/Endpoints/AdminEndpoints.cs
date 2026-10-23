using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Admin;
using TercerTiempo.Repository.Admin;

namespace TercerTiempo.Api.Endpoints;

internal static class AdminEndpoints
{
    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/admin/login", (AdminLoginCommand command, IAdminRepository admin, CancellationToken ct) => admin.LoginAsync(command, ct))
            .WithTags("Admin")
            .WithName("AdminLogin")
            .WithSummary("Inicio de sesión del administrador")
            .WithDescription("Solo usuarios con el rol Admin (Admin:UserName y Admin:Password de la configuración). Devuelve un JWT con el claim admin, independiente de las cuentas de jugador, técnico y club. Tras 5 fallos seguidos el usuario queda bloqueado 5 minutos.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        var group = app.MapGroup("/api/admin").WithTags("Admin").RequireAuthorization(AuthClaims.AdminPolicy);

        group.MapGet("/jobs", (string? provider, int? requests, IAdminRepository admin, CancellationToken ct) => admin.GetJobsAsync(provider, requests, ct))
            .WithName("GetJobs")
            .WithSummary("Trabajos en segundo plano y llamadas a webs externas")
            .WithDescription("Cada trabajo (datos de las ligas, actas públicas) con su intervalo, si está en marcha, cuándo toca el siguiente y su última ejecución; el historial de ejecuciones; los contadores de cada web (peticiones de la hora y el día frente a sus límites, pausas) y las últimas llamadas reales (requests, 200 por defecto, filtrables por provider). Solo administradores.");

        group.MapPost("/jobs/{job}/run", (string job, IAdminRepository admin, CancellationToken ct) => admin.RunJobAsync(job, ct))
            .WithName("RunJob")
            .WithSummary("Lanzar un trabajo ahora")
            .WithDescription("Lo pone en marcha sin esperar a su hora (football-data u official-sheets) y devuelve su estado. Los límites de peticiones de cada web se siguen respetando. 409 si ya está en marcha o está desactivado. Solo administradores.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/teams/{teamId:guid}/crest", (Guid teamId, ImportCrestRequest request, IAdminRepository admin, CancellationToken ct) => admin.ImportTeamCrestAsync(teamId, request.Url, ct))
            .WithName("ImportTeamCrest")
            .WithSummary("Importar el escudo de un equipo desde una URL")
            .WithDescription("Para los equipos que no tienen escudo guardado ni en el proveedor de datos: descarga la imagen (nunca de asturfutbol.es, de donde solo se leen actas) (png, jpg, gif, webp o svg, hasta 2 MB) y la guarda en Crests:Directory; a partir de entonces crestUrl apunta a GET /api/teams/{teamId}/crest. Solo administradores.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return app;
    }
}
