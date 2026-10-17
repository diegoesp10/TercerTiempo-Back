using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace TercerTiempo.Api.Infrastructure;

/// <summary>Título, descripción y descripción/orden de cada grupo (tag) del documento OpenAPI.</summary>
internal sealed class ApiInfoDocumentTransformer : IOpenApiDocumentTransformer
{
    // El orden define cómo se muestran los grupos en la referencia: sigue el flujo de uso.
    private static readonly (string Name, string Description)[] TagDescriptions =
    [
        ("Auth", "Alta e inicio de sesión. Devuelven el JWT que hay que enviar como `Authorization: Bearer <token>`."),
        ("Jugadores", "Perfil del jugador autenticado y su pertenencia a la plantilla de un equipo."),
        ("Competiciones", "Ligas sincronizadas desde futbolasturiano.es: nivel (nacional/regional), categoría y grupo. Públicas."),
        ("Equipos", "Equipos de cada competición, su plantilla (scrapeada del proveedor) y su calendario. Públicos."),
        ("Partidos", "Detalle de partidos y registro del rendimiento propio en partidos ya disputados."),
        ("Rendimiento", "Historial de rendimiento del jugador autenticado."),
        ("Estadísticas", "Panel del jugador: estadísticas individuales, impacto en el equipo y comparativa con la liga."),
        ("Liga", "Clasificación de la temporada activa. Pública."),
        ("Admin", "Operaciones de administración (sincronización con el proveedor de datos)."),
    ];

    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Info ??= new OpenApiInfo();
        document.Info.Title = "Tercer Tiempo API";
        document.Info.Version = "v1";
        document.Info.Description =
            """
            API de **Tercer Tiempo**: seguimiento de rendimiento para futbolistas amateur.

            **Flujo habitual**

            1. `POST /api/auth/register` (o `/login`) → devuelve el JWT.
            2. `GET /api/competitions` → elige liga y grupo; `GET /api/teams?competitionId=` → elige equipo;
               `GET /api/teams/{teamId}` → busca tu ficha en `roster` (si `rosterPublished`).
            3. `POST /api/players/me/team` con `teamId` y `rosterEntryId` → pertenencia verificada.
            4. `GET /api/teams/{teamId}/matches` → partidos disputados (`status = Played`).
            5. `PUT /api/matches/{matchId}/performance` → registra tu rendimiento.
            6. `GET /api/stats/dashboard` → tu panel.

            **Autenticación**: en *Authentication* elige `Bearer` y pega el `accessToken` (sin el prefijo `Bearer`).
            Los endpoints con candado requieren token; el resto son públicos.

            **Errores**: siempre `application/problem+json` (RFC 9457). 400 datos no válidos (con `errors` por campo),
            401 no autenticado, 403 sin permiso, 404 no encontrado, 409 conflicto, 422 regla de negocio incumplida.
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
