using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Admin;
using TercerTiempo.Application.Clubs.Invitations;
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

        group.MapGet("/football-sync", (IAdminRepository admin, CancellationToken ct) => admin.GetFootballSyncSettingsAsync(ct))
            .WithName("GetFootballSyncSettings")
            .WithSummary("Qué descarga la sincronización de las ligas")
            .WithDescription("La pasada automática (CurrentRound: solo la jornada en curso, de hoy menos daysBack a hoy más daysAhead; FullSeason: la temporada entera), si la jornada lleva plantillas, cada cuántos días toca la pasada completa y cuándo fue la última, la jornada en curso de hoy y las ligas sincronizadas. Solo administradores.");

        group.MapPut("/football-sync", (UpdateFootballSyncSettingsCommand command, IAdminRepository admin, CancellationToken ct) => admin.UpdateFootballSyncSettingsAsync(command, ct))
            .WithName("UpdateFootballSyncSettings")
            .WithSummary("Cambiar la sincronización automática de las ligas")
            .WithDescription("Se aplica desde la siguiente pasada. La pasada automática solo puede ser CurrentRound o FullSeason (unas fechas fijas se quedarían viejas). Solo administradores.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/football-sync/run", (RunFootballSyncCommand command, IAdminRepository admin, CancellationToken ct) => admin.RunFootballSyncAsync(command, ct))
            .WithName("RunFootballSync")
            .WithSummary("Sincronizar ahora las ligas, eligiendo qué")
            .WithDescription("scope: CurrentRound (la jornada en curso de los ajustes), DateRange (de from a to, hasta 366 días) o FullSeason. competitionIds limita a esas ligas (vacío = todas). includeRosters descarga también el catálogo de jugadores (por defecto: sí en la temporada completa; en la jornada, lo de los ajustes; en fechas, no). Con fechas solo se tocan los partidos de esos días, sus clasificaciones y los equipos nuevos; los partidos ocultos por la API solo se releen si ya estaban guardados. 409 si ya está en marcha. Solo administradores.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/licenses", (IAdminRepository admin, CancellationToken ct) => admin.GetLicensesAsync(ct))
            .WithName("GetLicensesOverview")
            .WithSummary("Licencias y cobros de todos los clubes")
            .WithDescription("Cada licencia con su estado de cobro (trial, trial-plan, active, grace, unpaid, cancelled, trial-ended), si da acceso, el plan, la próxima renovación y su importe, lo que debe y desde cuándo, y lo cobrado; un resumen (en prueba, al día, en gracia, impagadas, lo que se debe, lo cobrado y el ingreso mensual recurrente) y los últimos pagos. Solo administradores.");

        group.MapGet("/licensing-settings", (IAdminRepository admin, CancellationToken ct) => admin.GetLicensingSettingsAsync(ct))
            .WithName("GetLicensingSettings")
            .WithSummary("Planes, precios y prueba de las licencias")
            .WithDescription("Moneda, días de prueba y, por plan (Basic, Premium), precio mensual y anual en céntimos con IVA, técnicos admitidos e ids de precio de Stripe. provider es la pasarela del entorno (configuración, no editable aquí). Solo administradores.");

        group.MapPut("/licensing-settings", (UpdateLicensingSettingsCommand command, IAdminRepository admin, CancellationToken ct) => admin.UpdateLicensingSettingsAsync(command, ct))
            .WithName("UpdateLicensingSettings")
            .WithSummary("Cambiar planes, precios y prueba de las licencias")
            .WithDescription("Sustituye la moneda, los días de prueba y los dos planes. Vale para las altas, pruebas y renovaciones nuevas: las suscripciones ya creadas en Stripe siguen cobrando su precio hasta que se cambie su renovación (con la pasarela simulada, la próxima renovación ya cobra el precio nuevo). Premium no puede admitir menos técnicos que Básica; los ids de Stripe empiezan por price_. Solo administradores.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapGet("/club-invitations", (IAdminRepository admin, CancellationToken ct) => admin.GetClubInvitationsAsync(ct))
            .WithName("GetClubInvitations")
            .WithSummary("Invitaciones de clubes a la beta")
            .WithDescription("Todas, de la más reciente a la más antigua, con su estado (available, used, expired, revoked) y el club que la usó. Solo administradores.");

        group.MapGet("/club-invitations/clubs", (string? q, IAdminRepository admin, CancellationToken ct) => admin.SearchInvitationClubsAsync(q, ct))
            .WithName("SearchInvitationClubs")
            .WithSummary("Buscar clubes para invitarlos")
            .WithDescription("Hasta 20 clubes del catálogo cuyo nombre o el de uno de sus equipos contiene q (sin tildes ni mayúsculas; desde 2 letras), con sus equipos y ligas, la cuenta si ya se han dado de alta y su invitación (la disponible o, si no, la última). teamId es el que se manda al crear la invitación. Solo administradores.");

        group.MapPost("/club-invitations", (CreateClubInvitationCommand command, IAdminRepository admin, CancellationToken ct) => admin.CreateClubInvitationAsync(command, ct))
            .WithName("CreateClubInvitation")
            .WithSummary("Crear una invitación de club")
            .WithDescription("Genera un código de un solo uso para que un club se dé de alta durante la beta cerrada (Registration:ClubInviteRequired). note es para el administrador; expiresInDays, opcional (1-365). Solo administradores.")
            .ProducesValidationProblem();

        group.MapPost("/club-invitations/{id:guid}/revoke", (Guid id, IAdminRepository admin, CancellationToken ct) => admin.RevokeClubInvitationAsync(id, ct))
            .WithName("RevokeClubInvitation")
            .WithSummary("Anular una invitación de club")
            .WithDescription("Deja de servir para darse de alta. Una ya usada no se puede anular (422). Solo administradores.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

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
