# Tercer Tiempo · Backend

API de **Tercer Tiempo**, plataforma de seguimiento de rendimiento para futbolistas amateur. Un jugador se da de
alta, se une a la plantilla de su equipo (verificada contra la plantilla que publica un proveedor externo: BeSoccer,
FutbolAsturiano, ...) y, partido a partido, registra sus datos reales. Con eso la plataforma le dice **cuánto
aporta**: a nivel individual, a su equipo (resultados con él y sin él, cuota de goles, minutos) y en comparación con
el resto de la liga (equipo frente a la media y percentil frente a otros jugadores registrados).

El frontend vive en su propio repositorio: [TercerTiempo-Front](https://github.com/diegoesp10/TercerTiempo-Front).

## Arranque rápido

Requisitos: .NET 10 SDK.

```bash
dotnet run --project src/TercerTiempo.Api --launch-profile http
```

La API escucha en `http://localhost:5080`; la referencia OpenAPI en `http://localhost:5080/docs` y el documento en
`http://localhost:5080/openapi/v1.json`.
Al arrancar aplica las migraciones (SQLite, fichero `tercertiempo.db` junto al proyecto Api) y sincroniza las
ligas con el proveedor configurado. Por defecto usa **futbolasturiano.es** (datos reales de las ligas asturianas,
ver más abajo); sin red se puede cambiar a `FootballData:Provider = "fake"`, que genera tres ligas ficticias.

En `src/TercerTiempo.Api/TercerTiempo.Api.http` hay peticiones de ejemplo para todo el flujo.

## Documentación OpenAPI

La documentación la genera el soporte nativo de .NET (`Microsoft.AspNetCore.OpenApi`) a partir de los propios
endpoints y sus metadatos, sin Swashbuckle. Al arrancar, la raíz (`/`) redirige a la referencia.

| Ruta | Qué es |
|---|---|
| `/openapi/v1.json` | El documento **OpenAPI 3.1**: la fuente de verdad. Sirve para importarlo en Postman/Bruno/Insomnia o generar clientes tipados (`kiota`, `nswag`, `openapi-generator`). |
| `/docs` | Referencia interactiva renderizada con Scalar a partir del documento: endpoints agrupados por recurso en el orden del flujo de uso, cuerpo y parámetros de cada operación, respuestas posibles, ejemplos de cliente (C#/HttpClient por defecto, curl, fetch...) y autenticación Bearer para probar los endpoints protegidos. |

Cada operación lleva `operationId`, resumen, descripción con las reglas de negocio que aplica y los códigos de
error documentados; solo los endpoints protegidos muestran candado. Los campos de los cuerpos (`RegisterPlayerCommand`,
`LoginCommand`, `JoinTeamCommand`, `LogPerformanceRequest`) están descritos uno a uno con `[Description]`.
El documento se enriquece con transformadores (`Api/Infrastructure`): título y descripción, esquema Bearer y
candado/401 solo en operaciones con `RequireAuthorization()`.

Disponibilidad por entorno (sección `OpenApi` de la configuración):

- **Development**: siempre publicada y sin autenticación (salvo que configures `OpenApi:BasicAuth`, que entonces también aplica).
- **Otros entornos**: desactivada por defecto. Para exponerla en producción hay que habilitarla **y** protegerla con
  Basic Auth; sin credenciales no se publica (y se registra un aviso). Tanto `/docs` como `/openapi` piden usuario y
  contraseña:

```bash
OpenApi__Enabled=true OpenApi__BasicAuth__Username=docs OpenApi__BasicAuth__Password='una-contraseña-larga' dotnet run --project src/TercerTiempo.Api
```

`OpenApi:RoutePrefix` cambia la ruta de la referencia (por defecto `docs`).

## Capas

| Proyecto | Responsabilidad | Depende de |
|---|---|---|
| `TercerTiempo.Domain` | Entidades, value objects, reglas de negocio, eventos de dominio y contratos de persistencia (`IPlayerStore`, `IMatchStore`...). Sin frameworks. | — |
| `TercerTiempo.Application` | Casos de uso (commands/queries con handlers), DTOs, validación (FluentValidation), cálculo de estadísticas y abstracciones que implementa la infraestructura (`IIdentityService`, `ITokenService`, `ICurrentUser`, `IFootballDataProvider`). | Domain |
| `TercerTiempo.Repository` | Fachada entre la API y la aplicación: una interfaz por recurso (`IAuthRepository`, `IPlayersRepository`, `ITeamsRepository`, `IMatchesRepository`, `IPerformancesRepository`, `IStatsRepository`, `IAdminRepository`) que despacha el command/query correspondiente. Cada endpoint la llama en una línea. | Application |
| `TercerTiempo.Infrastructure` | EF Core + SQLite (stores), ASP.NET Identity, emisión de JWT, proveedores de datos de fútbol y el worker de sincronización. | Application |
| `TercerTiempo.Api` | Minimal APIs agrupadas por recurso, autenticación JWT, CORS, documento OpenAPI nativo + referencia Scalar, `ProblemDetails` global. | Repository, Infrastructure |

Flujo de una petición:

```
Endpoint ──(1 línea)──▶ Repository (fachada) ──▶ Application (validación + handler) ──▶ Domain
                                                        ▲
                                     Infrastructure (stores EF Core, Identity, proveedor de datos)
```

## Dominio

- **Player**: jugador registrado (vinculado al usuario de Identity). Agrega sus `SquadMembership`
  (pertenencia a un equipo en una temporada); solo una vigente a la vez. Si al unirse coincide con una ficha de
  la plantilla publicada nace **Active** y verificada; si el proveedor no publica plantilla para ese equipo nace
  **Active** sin verificar; si la publica y no aparece, queda **Pending** hasta que se valide.
- **Competition** (liga = categoría + grupo, con `Level`, `Tier`, `Group` y `SortOrder`) → **Season**;
  **Team**, **RosterEntry** (plantilla scrapeada), **Standing**.
- **Match**: partido sincronizado del proveedor, con resultado y estado. Sabe calcular resultado y
  puntos desde el punto de vista de cada equipo.
- **PlayerMatchPerformance**: una ficha por jugador y partido. `PerformancePolicy` decide quién puede
  registrarla (plantilla activa, partido disputado) y `PerformanceStats.EnsureValid()` las reglas de los
  datos (minutos, coherencia tiros/tiros a puerta, dos amarillas ⇒ roja...).

## Casos de uso (endpoints)

| Método | Ruta | Caso de uso |
|---|---|---|
| POST | `/api/auth/register` | Alta de jugador (crea usuario + Player, devuelve JWT) |
| POST | `/api/auth/login` | Login |
| GET | `/api/players/me` | Perfil y pertenencia actual |
| POST / DELETE | `/api/players/me/team` | Unirse a (reclamando ficha) / abandonar la plantilla |
| GET | `/api/competitions` | Ligas disponibles por nivel, categoría y grupo, con su temporada |
| GET | `/api/teams?competitionId=`, `/api/teams/{id}`, `/api/teams/{id}/matches` | Equipos de una liga, ficha con plantilla y jugadores registrados, calendario |
| GET | `/api/matches/{id}` | Detalle con los rendimientos registrados |
| PUT | `/api/matches/{id}/performance` | Registrar/actualizar mi rendimiento |
| GET | `/api/performances/me` | Mi historial |
| GET | `/api/stats/dashboard` | Panel: individual, impacto en el equipo, comparativa con la liga |
| GET | `/api/league/table?competitionId=` | Clasificación de una liga (sin parámetro, la del equipo del jugador) |
| POST | `/api/admin/sync` | Forzar sincronización con el proveedor |

Los endpoints no conocen los handlers: reciben la fachada de la capa Repository por inyección y la
llaman en una línea, p. ej. `(Guid teamId, ITeamsRepository teams, CancellationToken ct) => teams.GetAsync(teamId, ct)`.
Los handlers se registran por reflexión (`AddApplication`), las fachadas en `AddRepositoryLayer()`, y
los commands pasan por un decorador que ejecuta sus validadores. Las excepciones de dominio/aplicación se
traducen a `ProblemDetails` (400 validación, 401, 403, 404, 409, 422 regla de negocio).

## Ligas, grupos y proveedor de datos

Cada **competición** es una liga concreta (categoría + grupo) con su nivel y su propia temporada activa:

| Nivel | Competiciones (temporada 2026/27) |
|---|---|
| `National` | 1ª RFEF, 2ª RFEF, 3ª RFEF |
| `Regional` | 1ª AsturFútbol, 2ª AsturFútbol · Grupos 1-2, 3ª AsturFútbol · Grupos 1-4 |
| `Professional` | Primera y Segunda División (desactivadas por defecto) |
| `Cup` / `Youth` | Copa AsturFútbol, División de Honor juvenil (desactivadas por defecto) |

El jugador elige nivel → liga/grupo → equipo (`GET /api/competitions`, `GET /api/teams?competitionId=`).
Un jugador solo pertenece a una plantilla a la vez; la comparativa "frente a la liga" se hace siempre
contra la competición de su equipo.

`IFootballDataProvider` devuelve instantáneas (ligas con su temporada, equipos, partidos, clasificación y
plantillas); `SyncSeasonCommand` las vuelca al dominio de forma idempotente, liga a liga (un fallo en una
no impide sincronizar las demás). Se elige en `appsettings.json` → `FootballData:Provider`:

- **`futbolasturiano`** (por defecto): lee la API REST pública de [futbolasturiano.es](https://www.futbolasturiano.es)
  (WordPress + SportsPress; su `robots.txt` permite el acceso y se respeta un ritmo de una petición cada 150 ms
  identificándose con un User-Agent propio). Mapeo:
  - ligas y grupos → taxonomía `sp_league` (clasificadas por nombre con `LeagueClassifier`); temporada → `sp_season`
    (`FootballData:FutbolAsturiano:Season`, vacío = la más reciente);
  - equipos → `sp_team` por liga/temporada, con escudo embebido, más los que aparezcan en el calendario sin etiquetar;
  - partidos → `sp_event` (jornada `day`, goles en `results`; sin resultado = programado);
  - clasificación → `sportspress/v2/sp_table` (`data` por id de equipo);
  - plantillas → catálogo completo de `sp_player` agrupado por `current_teams` (una descarga por sincronización).
    La web publica plantillas en 1ª/2ª/3ª RFEF y 1ª AsturFútbol; en 2ª y 3ª AsturFútbol no, por lo que ahí la
    unión a un equipo queda **activa sin verificar** en lugar de pendiente.
  - Qué se sincroniza: `IncludeNational`, `IncludeRegional`, `IncludeProfessional`, `IncludeCups`, `IncludeYouth`
    y, para afinar, `IncludeLeagueIds` / `ExcludeLeagueIds` (ids de `sp_league`).
  - Una sincronización completa (10 ligas) son ~110 peticiones y tarda alrededor de un minuto.
- `fake`: tres ligas ficticias generadas en memoria (sin red), con plantillas solo en dos de ellas.
- `jsonfiles`: lee `competitions.json`, `competitions/{id}/{teams,matches,standings}.json` y
  `rosters/{teamId}.json` de `FootballData:JsonFiles:Directory`. Ideal para enchufar un scraper externo.
- `besoccer`: esqueleto para una única liga configurada; **hay que ajustar los `req` y los campos** al plan contratado.

El worker `FootballDataSyncWorker` sincroniza al arrancar y cada `SyncIntervalHours` horas; `POST /api/admin/sync` la fuerza.

## Configuración y secretos

- `Jwt:Key` (mínimo 32 caracteres) solo está definida en `appsettings.Development.json` para desarrollo.
  Para otros entornos usa user-secrets o variables de entorno:

```bash
dotnet user-secrets set "Jwt:Key" "una-clave-larga-y-aleatoria" --project src/TercerTiempo.Api
```

- `Cors:AllowedOrigins` incluye el frontend de Vite (`http://localhost:5173`).

## Migraciones y tests

```bash
dotnet ef migrations add NombreDeLaMigracion --project src/TercerTiempo.Infrastructure --startup-project src/TercerTiempo.Api --output-dir Persistence/Migrations
```

```bash
dotnet test
```

(`dotnet-ef` está como herramienta local: `dotnet tool restore` si hace falta.)

## Decisiones y siguientes pasos sugeridos

- **Identidad separada del dominio**: Identity gestiona credenciales; `Player` es el agregado de negocio.
- **Verificación de plantilla**: hoy el jugador reclama su ficha; falta un rol de capitán/administrador
  que valide pertenencias *Pending* y resuelva conflictos (dos personas reclamando la misma ficha).
- **Proveedor real**: implementar el contrato de BeSoccer o un scraper (p. ej. AngleSharp sobre
  FutbolAsturiano) detrás de `IFootballDataProvider`; el resto no cambia.
- **Estadísticas**: añadir evolución por jornada, vista colectiva de la plantilla y comparativa por posición.
- **Infra**: refresh tokens, PostgreSQL en lugar de SQLite, despliegue en contenedores, eventos de dominio
  con outbox (ya hay punto de extensión en `AppDbContext.SaveChangesAsync`).
