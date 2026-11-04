using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace TercerTiempo.Api.Infrastructure;

internal sealed class ApiInfoDocumentTransformer : IOpenApiDocumentTransformer
{
    // El orden define cómo se muestran los grupos en la referencia: sigue el flujo de uso.
    private static readonly (string Name, string Description)[] TagDescriptions =
    [
        ("Auth", "Alta (jugador o cuerpo técnico) e inicio de sesión. Devuelven el JWT que hay que enviar como `Authorization: Bearer <token>`."),
        ("Plantillas", "Lo que hace el cuerpo técnico: crear la plantilla de su equipo, gestionar staff y jugadores, decidir la visibilidad, registrar el acta de cada partido y consultar las estadísticas del grupo."),
        ("Jugadores", "Lo que hace un jugador: vincularse a su ficha con el código de invitación y consultar sus estadísticas."),
        ("Competiciones", "Ligas sincronizadas desde futbolasturiano.es: nivel (nacional/regional), categoría y grupo, y sus jornadas (la jornada en curso y cualquier otra). Públicas."),
        ("Equipos", "Equipos de cada competición, su plantilla publicada por el proveedor y su calendario. Públicos."),
        ("Partidos", "Detalle de partidos con las actas visibles."),
        ("Liga", "Clasificación de cada liga. Pública."),
        ("Pases", "Acceso de un partido (acta interna y vídeo) para quien no es de la plantilla: solicitudes, códigos de un solo uso, precios del club y reparto de lo vendido."),
        ("Vídeos", "Vídeos de los partidos: reproducción y descarga con ticket. Solo la plantilla y quien tenga un pase del partido."),
        ("Admin", "Administración: trabajos en segundo plano, llamadas a webs externas y escudos. Solo con el token de POST /api/admin/login."),
    ];

    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Info ??= new OpenApiInfo();
        document.Info.Title = "Tercer Tiempo API";
        document.Info.Version = "v1";
        document.Info.Description =
            """
            API de **Tercer Tiempo** (versión entrenador): el club nombra a su cuerpo técnico cada temporada, el cuerpo
            técnico registra el acta de cada partido jugador a jugador, la plataforma la convierte en estadísticas y la
            compara con el acta oficial de la federación (asturfutbol.es). Lo interno de cada plantilla (actas internas,
            estadísticas y vídeos) es **privado**: si se hace pública, cualquiera puede solicitar al club el acceso a un partido
            y solo lo ve quien canjee el pase de ese partido que le dé el club (gratis o con precio).

            **Flujo del club**

            1. `POST /api/auth/register` con `accountType = Club` (o `/login`) → JWT.
            2. `GET /api/competitions`, `GET /api/teams?competitionId=` y `POST /api/clubs/me/teams` → vincula tus equipos.
            3. `POST /api/clubs/me/appointments` → nombra a un técnico para un equipo y la temporada: obtienes su `accessCode`.
            4. `PUT` / `DELETE /api/clubs/me/appointments/{id}` → prorroga, acorta o revoca el nombramiento.

            **Flujo del cuerpo técnico**

            1. `POST /api/auth/register` con `accountType = Coach` y el `accessCode` del club → JWT y plantilla del equipo
               (se crea importando la publicada por futbolasturiano.es si existe).
               Con cuenta ya creada: `POST /api/coaches/me/appointments/redeem`.
            2. `GET /api/squads/{squadId}/matches` → partidos disputados; `PUT .../matches/{matchId}/sheet` → acta interna.
            3. `GET .../matches/{matchId}/sheet/comparison` → comparador con el acta oficial de la federación.
            4. `POST .../matches/{matchId}/video/uploads` → sube el vídeo del partido por partes; `GET /api/matches/{matchId}/videos`
               para verlo y `POST /api/videos/{id}/download-ticket` para descargarlo.
            5. `GET /api/squads/{squadId}/stats` → estadísticas; `PUT /api/squads/{squadId}/visibility` → acepta solicitudes de acceso.
            6. `POST .../matches/{matchId}/passes` → pase de un solo uso para el acta interna o el vídeo de ese partido;
               `GET /api/clubs/me/passes/requests` → solicitudes recibidas para aprobarlas.
            7. Club propietario o técnico vigente: `POST /api/squads/{squadId}/players/{squadPlayerId}/invite-code` → código personal de un solo uso para ese jugador.

            **Flujo del jugador**

            1. `POST /api/auth/register` con `accountType = Player`.
            2. `GET /api/auth/invite/{code}` → ve únicamente su ficha; `POST /api/auth/register` (Player, `accessCode` = ese código, sin nombre) → crea la cuenta con el nombre de la ficha y la vincula. Si ya tenía cuenta sin plantilla: `POST /api/players/me/squad` con `inviteCode`.
            3. `GET /api/players/me/dashboard` → tus estadísticas, tu impacto en el equipo y tu posición en la liga.

            **Pases por partido** (cualquier cuenta de fuera de la plantilla)

            1. `POST /api/passes/requests` → pide al club el acta interna o el vídeo de un partido de una plantilla pública.
            2. `GET /api/passes/{code}` y `POST /api/passes/{code}/redeem` → canjea el código (un solo uso); después lo ves en
               `GET /api/matches/{matchId}` y `GET /api/matches/{matchId}/videos`.

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
