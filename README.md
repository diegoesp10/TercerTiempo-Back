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

La API escucha en `http://localhost:5080` y la documentación interactiva (Scalar) en
`http://localhost:5080/scalar`. Al arrancar aplica las migraciones (SQLite, fichero `tercertiempo.db` junto al
proyecto Api) y sincroniza la liga con el proveedor configurado. Por defecto usa el proveedor **fake**: una Regional
Preferente ficticia de 12 equipos con calendario completo, resultados hasta la fecha actual, clasificación y
plantillas (determinista: los datos no cambian entre arranques y la liga "avanza" sola cada jornada).

En `src/TercerTiempo.Api/TercerTiempo.Api.http` hay peticiones de ejemplo para todo el flujo.

## Capas

| Proyecto | Responsabilidad | Depende de |
|---|---|---|
| `TercerTiempo.Domain` | Entidades, value objects, reglas de negocio, eventos de dominio y contratos de persistencia (`IPlayerStore`, `IMatchStore`...). Sin frameworks. | — |
| `TercerTiempo.Application` | Casos de uso (commands/queries con handlers), DTOs, validación (FluentValidation), cálculo de estadísticas y abstracciones que implementa la infraestructura (`IIdentityService`, `ITokenService`, `ICurrentUser`, `IFootballDataProvider`). | Domain |
| `TercerTiempo.Repository` | Fachada entre la API y la aplicación: una interfaz por recurso (`IAuthRepository`, `IPlayersRepository`, `ITeamsRepository`, `IMatchesRepository`, `IPerformancesRepository`, `IStatsRepository`, `IAdminRepository`) que despacha el command/query correspondiente. Cada endpoint la llama en una línea. | Application |
| `TercerTiempo.Infrastructure` | EF Core + SQLite (stores), ASP.NET Identity, emisión de JWT, proveedores de datos de fútbol y el worker de sincronización. | Application |
| `TercerTiempo.Api` | Minimal APIs agrupadas por recurso, autenticación JWT, CORS, OpenAPI + Scalar, `ProblemDetails` global. | Repository, Infrastructure |

Flujo de una petición:

```
Endpoint ──(1 línea)──▶ Repository (fachada) ──▶ Application (validación + handler) ──▶ Domain
                                                        ▲
                                     Infrastructure (stores EF Core, Identity, proveedor de datos)
```

## Dominio

- **Player**: jugador registrado (vinculado al usuario de Identity). Agrega sus `SquadMembership`
  (pertenencia a un equipo en una temporada). Si al unirse coincide con una ficha de la plantilla del
  proveedor nace **Active** (verificado); si no, **Pending** hasta que se valide.
- **Team**, **RosterEntry** (plantilla scrapeada), **Competition** → **Season**, **Standing**.
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
| GET | `/api/teams`, `/api/teams/{id}`, `/api/teams/{id}/matches` | Equipos, ficha con plantilla y jugadores registrados, calendario |
| GET | `/api/matches/{id}` | Detalle con los rendimientos registrados |
| PUT | `/api/matches/{id}/performance` | Registrar/actualizar mi rendimiento |
| GET | `/api/performances/me` | Mi historial |
| GET | `/api/stats/dashboard` | Panel: individual, impacto en el equipo, comparativa con la liga |
| GET | `/api/league/table` | Clasificación |
| POST | `/api/admin/sync` | Forzar sincronización con el proveedor |

Los endpoints no conocen los handlers: reciben la fachada de la capa Repository por inyección y la
llaman en una línea, p. ej. `(Guid teamId, ITeamsRepository teams, CancellationToken ct) => teams.GetAsync(teamId, ct)`.
Los handlers se registran por reflexión (`AddApplication`), las fachadas en `AddRepositoryLayer()`, y
los commands pasan por un decorador que ejecuta sus validadores. Las excepciones de dominio/aplicación se
traducen a `ProblemDetails` (400 validación, 401, 403, 404, 409, 422 regla de negocio).

## Proveedor de datos de fútbol

`IFootballDataProvider` devuelve instantáneas (competición/temporada, equipos, partidos, clasificación y
plantillas); `SyncSeasonCommand` las vuelca al dominio de forma idempotente. Se elige en
`appsettings.json` → `FootballData:Provider`:

- `fake` (por defecto): liga ficticia generada en memoria.
- `jsonfiles`: lee `competition.json`, `teams.json`, `matches.json`, `standings.json` y
  `rosters/{teamId}.json` de la carpeta `FootballData:JsonFiles:Directory`. Ideal para enchufar un scraper
  externo sin acoplarlo a la API.
- `besoccer`: esqueleto con `HttpClient` tipado y parseo tolerante; **hay que ajustar los `req` y los
  campos** a la documentación del plan contratado y configurar `ApiKey`, `CompetitionId` y `Season`.

El worker `FootballDataSyncWorker` sincroniza al arrancar y cada `SyncIntervalHours` horas.

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
