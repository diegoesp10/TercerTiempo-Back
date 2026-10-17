using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace TercerTiempo.Api.Infrastructure;

/// <summary>Título, descripción y descripción/orden de cada grupo (tag) del documento OpenAPI.</summary>
internal sealed class ApiInfoDocumentTransformer : IOpenApiDocumentTransformer
{
    // El orden define cómo se muestran los grupos en la referencia: sigue el flujo de uso.
    private static readonly (string Name, string Description)[] TagDescriptions =
    [
        ("Auth", "Alta (jugador o cuerpo técnico) e inicio de sesión. Devuelven el JWT que hay que enviar como `Authorization: Bearer <token>`."),
        ("Plantillas", "Lo que hace el cuerpo técnico: crear la plantilla de su equipo, gestionar staff y jugadores, decidir la visibilidad, registrar el acta de cada partido y consultar las estadísticas del grupo."),
        ("Jugadores", "Lo que hace un jugador: vincularse a su ficha con el código de invitación y consultar sus estadísticas."),
        ("Competiciones", "Ligas sincronizadas desde futbolasturiano.es: nivel (nacional/regional), categoría y grupo. Públicas."),
        ("Equipos", "Equipos de cada competición, su plantilla publicada por el proveedor y su calendario. Públicos."),
        ("Partidos", "Detalle de partidos con las actas visibles."),
        ("Liga", "Clasificación de cada liga. Pública."),
        ("Admin", "Operaciones de administración (sincronización con el proveedor de datos)."),
    ];

    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Info ??= new OpenApiInfo();
        document.Info.Title = "Tercer Tiempo API";
        document.Info.Version = "v1";
        document.Info.Description =
            """
            API de **Tercer Tiempo** (versión entrenador): el cuerpo técnico de cada equipo registra el acta de cada
            partido, jugador a jugador, y la plataforma convierte esas actas en estadísticas individuales y colectivas.
            Cada plantilla es **privada** salvo que su cuerpo técnico la haga pública.

            **Flujo del cuerpo técnico**

            1. `POST /api/auth/register` con `accountType = Coach` (o `/login`) → JWT.
            2. `GET /api/competitions` y `GET /api/teams?competitionId=` → localiza tu equipo.
            3. `POST /api/squads` con `teamId` → se crea la plantilla (importando la publicada por futbolasturiano.es si existe)
               y obtienes un `inviteCode` para el resto del staff y los jugadores.
            4. `GET /api/squads/{squadId}/matches` → partidos disputados; `PUT .../matches/{matchId}/sheet` → acta.
            5. `GET /api/squads/{squadId}/stats` → estadísticas de todos los jugadores.
            6. `PUT /api/squads/{squadId}/visibility` → hazla pública cuando quieras compartirla.

            **Flujo del jugador**

            1. `POST /api/auth/register` con `accountType = Player`.
            2. `GET /api/players/me/invite/{code}` → ve las fichas libres; `POST /api/players/me/squad` → vincúlate a la tuya.
            3. `GET /api/players/me/dashboard` → tus estadísticas, tu impacto en el equipo y tu posición en la liga.

            **Autenticación**: en *Authentication* elige `Bearer` y pega el `accessToken` (sin el prefijo `Bearer`).
            Los endpoints con candado requieren token; el resto son públicos.

            **Errores**: siempre `application/problem+json` (RFC 9457). 400 datos no válidos (con `errors` por campo),
            401 no autenticado, 403 sin permiso (plantilla privada o acción reservada al cuerpo técnico), 404 no encontrado,
            409 conflicto, 422 regla de negocio incumplida.
            """;

        var existing = (document.Tags ?? new HashSet<OpenApiTag>()).ToDictionary(t => t.Name ?? string.Empty);
        var ordered = new HashSet<OpenApiTag>();

        foreach (var (name, description) in TagDescriptions)
        {
            if (existing.Remove(name, out var tag))
                tag.Description = description;
            else
                tag = new OpenApiTag { Name = name, Description = description };

            ordered.Add(tag);
        }

        foreach (var leftover in existing.Values)
            ordered.Add(leftover);

        document.Tags = ordered;

        return Task.CompletedTask;
    }
}
