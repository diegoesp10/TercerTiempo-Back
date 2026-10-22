using TercerTiempo.Repository.Matches;

namespace TercerTiempo.Api.Endpoints;

internal static class MatchEndpoints
{
    public static IEndpointRouteBuilder MapMatchEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/matches").WithTags("Partidos");

        group.MapGet("/{matchId:guid}", (Guid matchId, IMatchesRepository matches, CancellationToken ct) => matches.GetAsync(matchId, ct))
            .WithName("GetMatch")
            .WithSummary("Detalle de un partido")
            .WithDescription("Datos del partido y las actas de las plantillas de ambos equipos. Cada acta solo se incluye si el usuario puede verla (plantilla pública o propia); si no, llega vacía con visible = false. Público.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/{matchId:guid}/preview", (Guid matchId, IMatchesRepository matches, CancellationToken ct) => matches.GetPreviewAsync(matchId, ct))
            .RequireAuthorization()
            .WithName("GetMatchPreview")
            .WithSummary("Previa del partido para el cuerpo técnico")
            .WithDescription("Solo cuentas de cuerpo técnico. Probabilidad de victoria local, empate y victoria visitante (modelo de Poisson con los goles a favor y en contra de la liga, la fuerza de cada equipo y la ventaja de jugar en casa) con las razones en texto; clasificación, forma reciente y balance como local/visitante de cada equipo; y, de las actas oficiales ya descargadas, los jugadores en forma de cada equipo, penaltis, goles en propia y tarjetas. Córners y balón parado no constan en las fuentes.")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/{matchId:guid}/official-sheet", (Guid matchId, bool? refresh, IMatchesRepository matches, CancellationToken ct) => matches.GetOfficialSheetAsync(matchId, refresh ?? false, ct))
            .WithName("GetOfficialSheet")
            .WithSummary("Acta oficial del partido (federación)")
            .WithDescription("Consulta la copia guardada en la base de datos. Las actas completas nunca se vuelven a descargar automáticamente. Solo las que faltan o siguen pendientes se encolan con límites compartidos y comprobaciones como máximo diarias. El parámetro refresh se conserva por compatibilidad y no fuerza una descarga. Público.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/{matchId:guid}/videos", (Guid matchId, IMatchesRepository matches, CancellationToken ct) => matches.ListVideosAsync(matchId, ct))
            .WithName("ListMatchVideos")
            .WithSummary("Vídeos del partido")
            .WithDescription("Vídeos subidos por el cuerpo técnico de cualquiera de los dos equipos, con el acceso del usuario a cada uno: Owner (su plantilla lo subió), Free, Purchased, Locked (técnico de otra plantilla: puede comprarlo) o None. Público, pero sin sesión nunca hay acceso.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }
}
