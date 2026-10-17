# Tercer Tiempo · Backend

API de **Tercer Tiempo**, plataforma de actas y rendimiento para el fútbol amateur. El **cuerpo técnico** de cada
equipo (entrenador, ayudante, delegado...) crea la plantilla de su equipo, importa las fichas que publica un proveedor
externo (futbolasturiano.es) o las añade a mano y, tras cada partido, registra el **acta**: minutos, goles,
asistencias, tiros, tarjetas y valoración de cada jugador. Cada **jugador** se vincula a su ficha con un código de
invitación y ve cuánto aporta: a nivel individual, a su equipo (resultados con él y sin él, cuota de goles, minutos)
y frente al resto de la liga. Los datos de cada plantilla son **privados** salvo que su cuerpo técnico la haga pública.

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
error documentados; solo los endpoints protegidos muestran candado. Los campos de los cuerpos (`RegisterCommand`,
`ClaimTeamCommand`, `JoinSquadStaffRequest`, `SquadPlayerRequest`, `MatchSheetRequest`, `JoinSquadAsPlayerCommand`...)
están descritos uno a uno con `[Description]`. El documento se enriquece con transformadores (`Api/Infrastructure`):
título y descripción con el flujo de uso, esquema Bearer y candado/401 solo en operaciones con `RequireAuthorization()`.

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
| `TercerTiempo.Domain` | Entidades, value objects, reglas de negocio, eventos de dominio y contratos de persistencia (`ISquadStore`, `IPlayerStore`, `ICoachStore`, `IMatchStore`...). Sin frameworks. | — |
| `TercerTiempo.Application` | Casos de uso (commands/queries con handlers), DTOs, validación (FluentValidation), cálculo de estadísticas, reglas de acceso a cada plantilla y abstracciones que implementa la infraestructura (`IIdentityService`, `ITokenService`, `ICurrentUser`, `IFootballDataProvider`). | Domain |
| `TercerTiempo.Repository` | Fachada entre la API y la aplicación: una interfaz por recurso (`IAuthRepository`, `ISquadsRepository`, `IPlayersRepository`, `ICompetitionsRepository`, `ITeamsRepository`, `IMatchesRepository`, `IStatsRepository`, `IAdminRepository`) que despacha el command/query correspondiente. Cada endpoint la llama en una línea. | Application |
| `TercerTiempo.Infrastructure` | EF Core + SQLite (stores), ASP.NET Identity, emisión de JWT, proveedores de datos de fútbol y el worker de sincronización. | Application |
| `TercerTiempo.Api` | Minimal APIs agrupadas por recurso, autenticación JWT, CORS, documento OpenAPI nativo + referencia Scalar, `ProblemDetails` global. | Repository, Infrastructure |

Flujo de una petición:

```
Endpoint ──(1 línea)──▶ Repository (fachada) ──▶ Application (validación + handler) ──▶ Domain
                                                        ▲
                                     Infrastructure (stores EF Core, Identity, proveedor de datos)
```

## Dominio

- **Cuentas**: una cuenta es de **jugador** (`Player`, con posición y fecha de nacimiento) o de **cuerpo técnico**
  (`Coach`). Identity guarda las credenciales; el JWT lleva el tipo de cuenta y el id del perfil
  (`account_type`, `player_id` / `coach_id`).
- **Squad** (plantilla): el agregado central y el **límite de privacidad**. Es la plantilla de un equipo en una
  temporada (una por equipo y temporada) y agrega:
  - `SquadStaff`: miembros del cuerpo técnico con su rol (`HeadCoach`, `AssistantCoach`, `Delegate`, `Physio`,
    `Analyst`, `Other`). Siempre queda al menos un primer entrenador.
  - `SquadPlayer`: fichas de los jugadores, importadas de la plantilla publicada por el proveedor (`RosterEntryId`)
    o añadidas a mano; sin nombres repetidos; una baja conserva sus actas. Cada ficha puede estar **vinculada** a una
    cuenta de jugador (`PlayerId`); un jugador solo está vinculado a una plantilla a la vez.
  - `Visibility`: nace **Private** (solo el cuerpo técnico y los jugadores vinculados ven actas y estadísticas);
    el cuerpo técnico puede hacerla **Public**.
  - `InviteCode`: código corto sin caracteres ambiguos con el que el resto del staff se incorpora y los jugadores se
    vinculan a su ficha; se puede regenerar.
- **Competition** (liga = categoría + grupo, con `Level`, `Tier`, `Group` y `SortOrder`) → **Season**;
  **Team**, **RosterEntry** (plantilla scrapeada), **Standing**.
- **Match**: partido sincronizado del proveedor, con resultado y estado. Sabe calcular resultado y
  puntos desde el punto de vista de cada equipo.
- **PlayerMatchPerformance**: una línea del acta por ficha y partido, registrada por un miembro del cuerpo técnico
  (`RecordedByCoachId`). `PerformancePolicy` decide quién puede registrarla (staff de la plantilla, partido de su
  temporada en el que juega su equipo, ya disputado) y `PerformanceStats.EnsureValid()` las reglas de los datos
  (minutos 0-120, coherencia tiros/tiros a puerta, dos amarillas ⇒ roja, sin minutos no hay acciones, valoración 1-10).

## Casos de uso (endpoints)

| Método | Ruta | Caso de uso |
|---|---|---|
| POST | `/api/auth/register` | Alta como jugador o cuerpo técnico (crea usuario + perfil, devuelve JWT) |
| POST | `/api/auth/login` | Login |
| GET | `/api/auth/me` | Cuenta autenticada: jugador con su plantilla, o técnico con sus plantillas |
| POST | `/api/squads` | Crear la plantilla de un equipo (importa la publicada por el proveedor; nace privada) |
| POST | `/api/squads/join` | Incorporarse al cuerpo técnico con el código de invitación |
| GET | `/api/squads/mine` | Plantillas del técnico autenticado |
| GET | `/api/squads/{id}` | Detalle: staff y fichas (el código solo lo ve el cuerpo técnico) |
| POST / PUT / DELETE | `/api/squads/{id}/players[/{squadPlayerId}]` | Añadir, modificar y dar de baja fichas |
| POST | `/api/squads/{id}/players/import` | Importar las fichas publicadas por el proveedor que falten |
| PUT | `/api/squads/{id}/visibility` | Hacer la plantilla pública o privada |
| POST | `/api/squads/{id}/invite-code` | Generar un nuevo código de invitación |
| GET | `/api/squads/{id}/matches` | Partidos del equipo con el número de líneas registradas en cada acta |
| GET / PUT | `/api/squads/{id}/matches/{matchId}/sheet` | Consultar / registrar el acta de un partido (idempotente; `played = false` borra la línea) |
| GET | `/api/squads/{id}/stats` | Estadísticas de temporada de cada jugador y su impacto en los resultados |
| GET | `/api/players/me/invite/{code}` | Plantilla que hay detrás de un código y sus fichas sin vincular |
| POST / DELETE | `/api/players/me/squad` | Vincularse a la ficha (por id o por nombre) / desvincularse |
| GET | `/api/players/me/dashboard` | Panel del jugador: individual, impacto en el equipo, comparativa con la liga |
| GET | `/api/players/me/performances` | Actas en las que aparece el jugador |
| GET | `/api/competitions` | Ligas disponibles por nivel, categoría y grupo, con su temporada |
| GET | `/api/teams?competitionId=`, `/api/teams/{id}`, `/api/teams/{id}/matches` | Equipos de una liga, ficha con plantilla publicada y estado de su plantilla en la plataforma, calendario |
| GET | `/api/matches/{id}` | Detalle con las actas de ambas plantillas (cada una solo si es visible para el usuario) |
| GET | `/api/league/table?competitionId=` | Clasificación de una liga (sin parámetro, la de la plantilla del usuario) |
| POST | `/api/admin/sync` | Forzar sincronización con el proveedor |

Las lecturas de una plantilla (`GET /api/squads/{id}`, sus partidos, actas y estadísticas) son públicas si la
plantilla es pública y, si es privada, solo para su cuerpo técnico y sus jugadores vinculados (403 en otro caso).
Las escrituras son siempre del cuerpo técnico. La comparativa "frente a la liga" de un jugador solo usa actas que
él puede ver: las de plantillas públicas de su liga y la suya.

Los endpoints no conocen los handlers: reciben la fachada de la capa Repository por inyección y la
llaman en una línea, p. ej. `(Guid squadId, ISquadsRepository squads, CancellationToken ct) => squads.GetStatsAsync(squadId, ct)`.
Los handlers se registran por reflexión (`AddApplication`), las fachadas en `AddRepositoryLayer()`, y
los commands pasan por un decorador que ejecuta sus validadores. Las excepciones de dominio/aplicación se
traducen a `ProblemDetails` (400 validación o cuerpo malformado, 401, 403, 404, 409, 422 regla de negocio).

## Ligas, grupos y proveedor de datos

Cada **competición** es una liga concreta (categoría + grupo) con su nivel y su propia temporada activa:

| Nivel | Competiciones (temporada 2026/27) |
|---|---|
| `National` | 1ª RFEF, 2ª RFEF, 3ª RFEF |
| `Regional` | 1ª AsturFútbol, 2ª AsturFútbol · Grupos 1-2, 3ª AsturFútbol · Grupos 1-4 |
| `Professional` | Primera y Segunda División (desactivadas por defecto) |
| `Cup` / `Youth` | Copa AsturFútbol, División de Honor juvenil (desactivadas por defecto) |

El cuerpo técnico elige nivel → liga/grupo → equipo (`GET /api/competitions`, `GET /api/teams?competitionId=`) y
crea su plantilla; la comparativa "frente a la liga" se hace siempre contra la competición del equipo.

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
    plantilla nace vacía y el cuerpo técnico añade las fichas a mano.
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

Ojo con SQLite: no admite `ORDER BY` sobre columnas `DateTimeOffset`; si hay que ordenar por fecha se hace en
memoria (ver `SquadStore.GetByCoachAsync`). Al pasar a SQL Server deja de ser necesario.

## Decisiones y siguientes pasos sugeridos

- **Identidad separada del dominio**: Identity gestiona credenciales; `Player` y `Coach` son los agregados de negocio.
- **La plantilla es el límite de privacidad**: actas y estadísticas pertenecen a la `Squad`; nada sale de ella
  salvo que su cuerpo técnico la haga pública. No hay validación externa de pertenencias: el código de invitación
  lo controla el propio cuerpo técnico (y puede regenerarlo).
- **Roles del staff**: hoy todos los miembros del cuerpo técnico pueden registrar actas y gestionar la plantilla;
  queda por decidir si algunas acciones (visibilidad, bajas de staff) se reservan al primer entrenador.
- **Estadísticas**: añadir evolución por jornada, comparativa por posición e importar las estadísticas oficiales por
  jugador que publica SportsPress en los eventos jugados para precargar el acta.
- **Infra**: refresh tokens, SQL Server en lugar de SQLite, despliegue en contenedores, eventos de dominio
  con outbox (ya hay punto de extensión en `AppDbContext.SaveChangesAsync`).
