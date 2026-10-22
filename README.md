# Tercer Tiempo · Backend

API de **Tercer Tiempo**, plataforma de seguimiento del rendimiento de cada jugador, partido a partido. Cada **club** da de alta a los
técnicos de sus equipos temporada a temporada mediante **nombramientos** con un código de acceso. El **cuerpo técnico**
nombrado gestiona la plantilla de su equipo (fichas importadas de futbolasturiano.es o añadidas a mano) y, tras cada
partido, registra el **acta interna**: minutos, goles, asistencias, tiros, tarjetas... de cada jugador; la **nota** no
la pone nadie: se calcula con esos datos. Esa acta se **compara con el acta oficial** que publica la federación
(asturfutbol.es) y, si el equipo graba el partido, el cuerpo técnico puede **subir el vídeo**, que su plantilla ve
gratis y el resto de técnicos previo pago. Cada **jugador** se vincula a su ficha con un código de invitación y ve
cuánto aporta: a nivel individual, a su equipo y frente al resto de la liga. Los datos de cada plantilla son
**privados** salvo que su cuerpo técnico la haga pública.

El frontend vive en su propio repositorio: [TercerTiempo-Front](https://github.com/diegoesp10/TercerTiempo-Front).

## Arranque rápido

Requisitos: .NET 10 SDK y una instancia de **SQL Server** (2019 o posterior; vale Express, Developer o LocalDB).

```bash
dotnet run --project src/TercerTiempo.Api --launch-profile http
```

La API escucha en `http://localhost:5080`; la referencia OpenAPI en `http://localhost:5080/docs` y el documento en
`http://localhost:5080/openapi/v1.json`.
Al arrancar crea la base de datos si no existe, aplica las migraciones pendientes y sincroniza las ligas con el
proveedor configurado: una base de datos vacía queda lista para usar con los datos reales en unos minutos. La cadena
de conexión de desarrollo (`appsettings.Development.json`) apunta a la instancia local por defecto con autenticación de
Windows: `Server=localhost;Database=TercerTiempo;Trusted_Connection=True;TrustServerCertificate=True;Encrypt=True`.
Para otra instancia (`localhost\SQLEXPRESS`, `(localdb)\MSSQLLocalDB`) o con usuario y contraseña, cámbiala ahí o en
`ConnectionStrings__Default` (ver *Configuración y secretos*). Por defecto usa **futbolasturiano.es** (datos reales de las ligas asturianas,
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
`CreateAppointmentRequest`, `RedeemAppointmentCommand`, `SquadPlayerRequest`, `MatchSheetRequest`, `JoinSquadAsPlayerCommand`...)
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
| `TercerTiempo.Domain` | Entidades, value objects, reglas de negocio, eventos de dominio y contratos de persistencia (`ISquadStore`, `IClubStore`, `ICoachAppointmentStore`, `IOfficialSheetStore`, `IMatchVideoStore`...). Sin frameworks. | — |
| `TercerTiempo.Application` | Casos de uso (commands/queries con handlers), DTOs, validación (FluentValidation), cálculo de estadísticas, reglas de acceso a cada plantilla, comparador de actas y abstracciones que implementa la infraestructura (`IIdentityService`, `ITokenService`, `ICurrentUser`, `IFootballDataProvider`, `IOfficialSheetProvider`, `IVideoStorage`, `IPaymentGateway`, `IVideoTickets`). | Domain |
| `TercerTiempo.Repository` | Fachada entre la API y la aplicación: una interfaz por recurso (`IAuthRepository`, `IClubsRepository`, `ICoachesRepository`, `ISquadsRepository`, `IPlayersRepository`, `ICompetitionsRepository`, `ITeamsRepository`, `IMatchesRepository`, `IVideosRepository`, `IStatsRepository`, `IAdminRepository`) que despacha el command/query correspondiente. Cada endpoint la llama en una línea. | Application |
| `TercerTiempo.Infrastructure` | EF Core + SQL Server (stores), ASP.NET Identity, emisión de JWT, proveedores de datos de fútbol y el worker de sincronización, lector de actas de asturfutbol.es, almacenamiento de vídeos en disco, tickets de reproducción y pasarela de pago simulada. | Application |
| `TercerTiempo.Api` | Minimal APIs agrupadas por recurso, autenticación JWT, CORS, documento OpenAPI nativo + referencia Scalar, `ProblemDetails` global. | Repository, Infrastructure |

Flujo de una petición:

```
Endpoint ──(1 línea)──▶ Repository (fachada) ──▶ Application (validación + handler) ──▶ Domain
                                                        ▲
                                     Infrastructure (stores EF Core, Identity, proveedor de datos)
```

## Dominio

- **Cuentas**: una cuenta es de **jugador** (`Player`, con posición y fecha de nacimiento), de **cuerpo técnico**
  (`Coach`) o de **club** (`Club`). Identity guarda las credenciales; el JWT lleva el tipo de cuenta y el id del perfil
  (`account_type`, `player_id` / `coach_id` / `club_id`).
- **Escudos siempre propios**: `Team` no guarda ninguna URL de terceros; solo `CrestKey`, el archivo
  "{id}-{versión}.{ext}" de `Crests:Directory` que la API sirve en `GET /api/teams/{id}/crest?v=` (la versión cambia
  con cada escudo nuevo e invalida la caché). La sincronización de datos descarga el escudo que publica el proveedor al
  crear el equipo; la federación aporta el suyo; el club puede sustituir el de sus equipos; y para el resto está la
  importación por URL de administración. Un equipo sin escudo muestra sus iniciales en el frontal.
- **Ficha y escudo del equipo** (`Team.Profile`, `Team.CrestKey`): `SyncTeamProfilesCommand` localiza cada equipo de
  la temporada en curso en asturfutbol.es (código del equipo por las primeras jornadas de su competición), descarga su
  ficha (`ITeamProfileProvider`: nombre oficial, sede, web, equipación, campo donde juega de local con su dirección) y
  su escudo, que se guarda en `Crests:Directory` (`ICrestStorage`) y se sirve desde la API para no enlazar imágenes de
  terceros. Se ejecuta tras cada sincronización del worker y se refresca cada `Crests:RefreshDays` días. Los equipos sin
  escudo en la federación (los de fuera de Asturias) lo descargan del proveedor de datos si lo publica, y para el resto
  hay `POST /api/admin/teams/{id}/crest` con la URL de la imagen; en el frontal, un equipo sin escudo muestra una
  insignia con sus iniciales.
- **Club**: solo puede haber una cuenta por club (`NormalizedName` único). Al darse de alta elige su nombre del
  **catálogo de clubes** (`GET /api/clubs/catalog`, `ClubCatalog`: los equipos del proveedor agrupados por las palabras
  significativas del nombre, primer equipo + filiales) y quedan vinculados todos sus equipos (`ClubTeam`; un equipo solo
  puede pertenecer a un club). Puede vincular a mano otro equipo si su nombre se corresponde con el del club. Emite
  **CoachAppointment** (nombramientos): para un equipo y una temporada, con rol, nombre del técnico, un **código de
  acceso** de un solo uso y un periodo de validez que nunca pasa del final de la temporada. Un técnico solo puede darse
  de alta con un código (`RegisterCommand.AccessCode`) o canjearlo desde su cuenta; al hacerlo entra en el cuerpo
  técnico de la plantilla del equipo (que se crea si no existe) hasta la fecha de validez. El club puede acortar,
  prorrogar (dentro de la temporada) o revocar el nombramiento; al revocarlo el técnico sale del staff.
- **Squad** (plantilla): el agregado central y el **límite de privacidad**. Es la plantilla de un equipo en una
  temporada (una por equipo y temporada) y agrega:
  - `SquadStaff`: miembros del cuerpo técnico con su rol (`HeadCoach`, `AssistantCoach`, `Delegate`, `Physio`,
    `Analyst`, `Other`), el nombramiento que los trajo y su `ValidUntil`: todas las comprobaciones de "es staff"
    (`Squad.IsStaff(coachId, today)`, `SquadAccess`) descartan a quien ha causado baja **o ha caducado**.
  - `SquadPlayer`: fichas de los jugadores, importadas de la plantilla publicada por el proveedor (`RosterEntryId`)
    o añadidas a mano; sin nombres repetidos; una baja conserva sus actas. Cada ficha puede estar **vinculada** a una
    cuenta de jugador (`PlayerId`); un jugador solo está vinculado a una plantilla a la vez.
  - `Visibility`: nace **Private** (solo el cuerpo técnico y los jugadores vinculados ven actas y estadísticas);
    el cuerpo técnico puede hacerla **Public**.
  - Cada `SquadPlayer` tiene un `InviteCode` personal de 12 caracteres, inicialmente vacío. Lo genera el club propietario
    o un técnico vigente para esa ficha. Exige el mismo nombre y apellidos de la cuenta (ignorando tildes, mayúsculas y separadores),
    se consume al vincular y se invalida al regenerarlo, dar de baja o cambiar el nombre de la ficha. No hay código compartido de plantilla.
- **Competition** (liga = categoría + grupo, con `Level`, `Tier`, `Group` y `SortOrder`) → **Season**;
  **Team**, **RosterEntry** (plantilla scrapeada), **Standing**.
- **Match**: partido sincronizado del proveedor, con resultado y estado. Sabe calcular resultado y
  puntos desde el punto de vista de cada equipo. `OfficialRef` guarda su acta en la federación una vez localizada
  (también `Season.OfficialRef` y `Team.OfficialRef` con los códigos de asturfutbol.es).
- **OfficialMatchSheet**: copia del **acta oficial** de un partido (asturfutbol.es): árbitro, marcador, una línea por
  convocado de cada equipo (dorsal, titular, minuto de entrada y salida, minutos jugados, goles, goles de penalti y en
  propia puerta, amarillas, roja) y los sucesos en orden (goles, tarjetas, cambios). `Pending` mientras la federación
  no publique las alineaciones; `Available` después.
- **MatchVideo** y **VideoPurchase**: vídeo de un partido subido por el cuerpo técnico de una plantilla (archivo en el
  almacenamiento, precio) y los accesos comprados por técnicos de otras plantillas.
- **PlayerMatchPerformance**: una línea del acta por ficha y partido, registrada por un miembro del cuerpo técnico
  (`RecordedByCoachId`). `PerformancePolicy` decide quién puede registrarla (staff de la plantilla, partido de su
  temporada en el que juega su equipo, ya disputado) y `PerformanceStats.EnsureValid()` las reglas de los datos
  (minutos 0-120, coherencia tiros/tiros a puerta, dos amarillas ⇒ roja, sin minutos no hay acciones).
- **PerformanceRating**: la **nota** de cada línea se calcula solo con datos y se guarda al registrar el acta (también
  se recalcula si cambia la posición del jugador). Parte de 6,0 y suma o resta por bloques, con pesos según la posición
  (un gol vale más a un defensa que a un delantero; la portería a cero, sobre todo a porteros y defensas):

  | Bloque | Qué cuenta |
  |---|---|
  | Ataque | goles y tiros a puerta |
  | Creación | asistencias y pases clave |
  | Defensa | entradas, intercepciones; para defensas y medios, portería a cero (60' o más) y goles encajados |
  | Portería | paradas; para porteros, portería a cero y goles encajados |
  | Disciplina | −0,3 por amarilla; −1,5 si hay roja |
  | Resultado | victoria +0,4, empate +0,1, derrota −0,3, proporcional a los minutos |

  Escala 3,0-10,0 con un decimal; sin minutos no hay nota. Las notas manuales de versiones anteriores se vacían en la
  migración `ResetManualRatings` y la API las recalcula al arrancar (`RecalculateMissingRatingsCommand`).

## Casos de uso (endpoints)

| Método | Ruta | Caso de uso |
|---|---|---|
| POST | `/api/auth/register` | Alta como jugador, cuerpo técnico (con el código de acceso del club: lo canjea y entra en su plantilla) o club |
| POST | `/api/auth/login` | Login |
| GET | `/api/auth/me` | Cuenta autenticada: jugador con su plantilla; técnico con sus plantillas y nombramientos; club con sus equipos y nombramientos |
| GET | `/api/sync/status` | Estado de la sincronización (fase, liga en curso, progreso, última ejecución, siguiente); el frontal lo sondea. Público |
| POST | `/api/admin/sync/team-profiles?force=` | Localiza cada equipo en asturfutbol.es y descarga su ficha (sede, web, equipación, campo) y su escudo; sin ficha, el escudo del proveedor |
| POST | `/api/admin/teams/{id}/crest` | Importar el escudo de un equipo desde una URL (`{ "url": ... }`) |
| GET | `/api/clubs/catalog?search=` | Catálogo de clubes del proveedor (equipos agrupados, si ya tienen cuenta); de aquí sale el nombre del alta. Público |
| GET | `/api/clubs/me` | El club: equipos vinculados y nombramientos (el código solo mientras está pendiente) |
| POST / DELETE | `/api/clubs/me/teams[/{teamId}]` | Vincular otro equipo del proveedor (nombre acorde al club) / desvincular |
| PUT | `/api/clubs/me/teams/{teamId}/crest` | El club cambia el escudo de uno de sus equipos (multipart `file`, hasta 2 MB) |
| POST / PUT / DELETE | `/api/clubs/me/teams/{teamId}/roster[/{entryId}]` | El club añade, corrige o retira fichas de la plantilla publicada (lo que toca el club no lo sobrescribe el proveedor; las del proveedor retiradas se ocultan) |
| GET | `/api/matches/{id}/preview` | Previa para el cuerpo técnico: probabilidades (Poisson sobre goles a favor/en contra, fuerza de cada equipo y ventaja local) con razones, forma, balance local/visitante, jugadores en forma y disciplina del rival según las actas oficiales |
| POST | `/api/clubs/me/appointments` | Nombrar a un técnico para un equipo y la temporada en curso → código de acceso |
| PUT / DELETE | `/api/clubs/me/appointments/{id}` | Acortar o prorrogar la validez / revocar (el técnico sale del staff) |
| POST | `/api/coaches/me/appointments/redeem` | Técnico con cuenta: canjear otro código (nueva temporada, otro equipo) |
| GET | `/api/squads/mine` | Plantillas con nombramiento vigente del técnico autenticado |
| GET | `/api/squads/{id}` | Detalle: staff y fichas; `canInvitePlayers` y códigos personales solo para club propietario y técnicos vigentes |
| POST / PUT / DELETE | `/api/squads/{id}/players[/{squadPlayerId}]` | Añadir, modificar y dar de baja fichas |
| POST | `/api/squads/{id}/players/import` | Importar las fichas publicadas por el proveedor que falten |
| PUT | `/api/squads/{id}/visibility` | Hacer la plantilla pública o privada |
| POST | `/api/squads/{id}/players/{squadPlayerId}/invite-code` | Club propietario o técnico vigente: generar o renovar el código personal de esa ficha |
| GET | `/api/squads/{id}/matches` | Partidos del equipo con el número de líneas registradas en cada acta |
| GET / PUT | `/api/squads/{id}/matches/{matchId}/sheet` | Consultar / registrar el acta interna de un partido (idempotente; `played = false` borra la línea) |
| GET | `/api/squads/{id}/matches/{matchId}/sheet/comparison` | **Comparador**: acta interna frente a la oficial, jugador a jugador, con las discrepancias (`?refresh=true` vuelve a pedirla a la federación) |
| POST | `/api/squads/{id}/matches/{matchId}/video` | Subir el vídeo del partido (multipart `file` + `title`) |
| GET | `/api/squads/{id}/stats` | Estadísticas de temporada de cada jugador y su impacto en los resultados |
| GET | `/api/squads/{id}/players/{squadPlayerId}/profile` | Ficha para gráficos: serie partido a partido con el desglose de la nota, perfil por 90' y media/máximo de la plantilla |
| GET | `/api/squads/{id}/timeline` | Evolución del equipo: resultado, puntos acumulados, nota media y mejor nota de cada partido |
| GET | `/api/players/me/invite/{code}` | Solo la ficha asociada al código personal, si coincide el nombre de la cuenta; nunca devuelve la plantilla completa |
| POST / DELETE | `/api/players/me/squad` | Vincularse con `{ "inviteCode": "..." }` (un solo uso, sin elegir id de ficha) / desvincularse |
| GET | `/api/players/me/dashboard` | Panel del jugador: individual, impacto en el equipo, comparativa con la liga |
| GET | `/api/players/me/performances` | Actas en las que aparece el jugador |
| GET | `/api/competitions` | Ligas disponibles por nivel, categoría y grupo, con su temporada |
| GET | `/api/competitions/{id}/matchdays/current`, `/api/competitions/{id}/matchdays/{n}` | Partidos de la jornada en curso (o la próxima) o de cualquier otra, con la lista de jornadas y sus fechas |
| GET | `/api/teams?competitionId=`, `/api/teams/{id}`, `/api/teams/{id}/matches` | Equipos de una liga, ficha con plantilla publicada y estado de su plantilla en la plataforma, calendario |
| GET | `/api/teams/{id}/crest` | Escudo guardado (descargado de la federación); `crestUrl` apunta aquí cuando existe. Público |
| GET | `/api/matches/{id}` | Detalle con las actas de ambas plantillas (cada una solo si es visible para el usuario) |
| GET | `/api/matches/{id}/official-sheet` | Acta oficial de la federación (o `NotPlayed` / `Pending` / `Unavailable`); público |
| GET | `/api/matches/{id}/videos` | Vídeos del partido con el acceso del usuario a cada uno |
| POST | `/api/videos/{id}/purchase`, `/api/videos/{id}/ticket` | Comprar el acceso (técnicos de otras plantillas) / ticket de reproducción |
| GET | `/api/videos/{id}/stream?ticket=` | Streaming con rangos (206) para el reproductor |
| DELETE | `/api/videos/{id}` | Eliminar el vídeo (solo el cuerpo técnico que lo subió) |
| GET | `/api/league/table?competitionId=` | Clasificación de una liga (sin parámetro, la de la plantilla del usuario) |
| POST | `/api/admin/sync` | Forzar sincronización con el proveedor |

Las lecturas de una plantilla (`GET /api/squads/{id}`, sus partidos, actas, comparador y estadísticas) son públicas
si la plantilla es pública y, si es privada, solo para su cuerpo técnico vigente y sus jugadores vinculados (403 en
otro caso). Las escrituras son siempre del cuerpo técnico vigente: un nombramiento caducado o revocado deja de poder
editar y, si la plantilla es privada, de ver. La comparativa "frente a la liga" de un jugador solo usa actas que
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
  - partidos → `sp_event` (jornada `day`, goles en `results`; sin resultado = programado). La fecha real es `date`
    (hora peninsular, `FootballData:FutbolAsturiano:TimeZone`); `date_gmt` se queda a menudo con la fecha provisional
    del calendario (domingo a las 12:00) y no se usa;
  - **partidos ocultos por la API**: WordPress marca como "programados" (`future`) los partidos cuya fecha de
    publicación aún no ha llegado, y la API pública no los devuelve aunque la web los muestra. Afecta a la jornada en
    curso (se publica el domingo a las 12:00) y, en algunas ligas como 3ª AsturFútbol, a casi todo el calendario
    futuro. La cabecera `X-WP-Total` sí los cuenta, y cada liga ocupa un bloque de ids consecutivos, así que los
    huecos del bloque son exactamente esos partidos: se leen de su página pública (`/?p={id}`, con AngleSharp) para
    sacar jornada, fecha y hora, equipos, resultado y campo (`HiddenEventPlanner`, `EventPageParser`). Cada página
    tarda segundos, así que los ya guardados se reutilizan, solo se releen los que se juegan en los próximos
    `HiddenEventRefreshDays` días (hora definitiva, resultado) y las lecturas nuevas tienen un tope por liga
    (`HiddenEventPagesPerLeague`); el resto llega en las siguientes sincronizaciones, empezando por las jornadas
    más próximas. `RecoverHiddenEvents = false` lo desactiva;
  - clasificación → `sportspress/v2/sp_table` (`data` por id de equipo);
  - plantillas → catálogo completo de `sp_player` agrupado por `current_teams` (una descarga por sincronización).
    La web publica plantillas en 1ª/2ª/3ª RFEF y 1ª AsturFútbol; en 2ª y 3ª AsturFútbol no, por lo que ahí la
    plantilla nace vacía y el cuerpo técnico añade las fichas a mano.
  - Qué se sincroniza: `IncludeNational`, `IncludeRegional`, `IncludeProfessional`, `IncludeCups`, `IncludeYouth`
    y, para afinar, `IncludeLeagueIds` / `ExcludeLeagueIds` (ids de `sp_league`).
  - Una sincronización completa (10 ligas) son ~110 peticiones a la API más las páginas de partidos ocultos: la
    primera, con la base de datos vacía, tarda varios minutos; las siguientes, uno o dos.
- `fake`: tres ligas ficticias generadas en memoria (sin red), con plantillas solo en dos de ellas.
- `jsonfiles`: lee `competitions.json`, `competitions/{id}/{teams,matches,standings}.json` y
  `rosters/{teamId}.json` de `FootballData:JsonFiles:Directory`. Ideal para enchufar un scraper externo.
- `besoccer`: esqueleto para una única liga configurada; **hay que ajustar los `req` y los campos** al plan contratado.

El worker `FootballDataSyncWorker` sincroniza al arrancar y cada `SyncIntervalHours` horas; `POST /api/admin/sync` la fuerza.

## Acta oficial: asturfutbol.es

La web de la RFFPA ([asturfutbol.es](https://www.asturfutbol.es)) publica el acta de cada partido (alineaciones,
sustituciones con minuto, goles y tarjetas). No tiene API: `AsturfutbolOfficialSheetProvider` lee sus páginas
(`Infrastructure/OfficialSheets/Asturfutbol`, AngleSharp) con un ritmo de una petición cada dos segundos
(`OfficialSheets:Asturfutbol:RequestDelayMs`; tras cientos de peticiones seguidas la web responde vacío un buen rato,
que el cliente trata como fallo transitorio) y User-Agent propio. Particularidades:

- Las páginas son públicas pero exigen una cookie de sesión: la primera petición redirige a `/pnfg/NLogin`, que la fija
  y devuelve a la página pedida (`AsturfutbolClient` usa un `HttpClientHandler` con cookies y redirecciones). Sin cookie
  responde una página vacía ("No se ha aceptado el cookie"). Las páginas no declaran charset (ISO-8859-1).
- Su `robots.txt` prohíbe el rastreo a los bots genéricos; aquí no se rastrea: solo se leen, bajo demanda y con caché,
  las páginas de los partidos que alguien consulta.
- Los marcadores llevan dígitos ofuscados (clases `fa-N` y `<span>` ocultos con cifras señuelo): no se usan; el
  resultado del partido viene de futbolasturiano.es y los goles se cuentan por goleador.
- Localización de un acta (`OfficialSheetLocator`): la competición se busca por nombre en los desplegables del
  calendario (`NFG_CmpJornada`, con alias para "Tercera Federación" = 3ª RFEF) y se guarda en `Season.OfficialRef`
  como `competición/grupo/temporada`; la jornada (`NFG_CmpJornada?...&CodJornada=`) lista los partidos con el botón
  del acta (`CodActa`), que se emparejan con los nuestros por parecido de nombres de equipo (`TeamNameSimilarity`:
  palabras significativas y letra del filial) y quedan guardados en `Match.OfficialRef` (y los códigos de equipo en
  `Team.OfficialRef`) para no volver a buscarlos.
- Lectura del acta (`AsturfutbolPages.ParseSheet`): titulares y suplentes por equipo con dorsal, sustituciones (fila
  "entra" / fila "sale" con el minuto), tarjetas (por la imagen `tarj_amar` / `tarj_roja`) y goles (tipo por el título
  del icono: normal, penalti, propia puerta; se atribuyen al equipo del goleador). Los minutos jugados se calculan:
  titular desde el 0, suplente desde su entrada, hasta la sustitución, la expulsión o el final
  (`OfficialSheets:Asturfutbol:MatchLength`). Los nombres vienen como "APELLIDOS, NOMBRE" y, en menores, a veces solo
  el nombre de pila.
- Cuándo se consulta (`SyncOfficialSheetCommand`): nunca antes del saque; mientras no haya alineaciones (partido en
  juego o recién terminado) como mucho cada 10 minutos (`Pending`, el frontal lo reintenta solo); publicada, una vez
  al día durante la semana siguiente por si la corrigen; `?refresh=true` fuerza la descarga. `Unavailable` si no se
  localiza el partido (liga sin correspondencia, nombres muy distintos).
- Comparador (`CompareMatchSheetCommand`, `SheetMatcher`): cada línea oficial del equipo de la plantilla se empareja
  con una ficha por las palabras del nombre (sin orden ni tildes) y, en caso de duda, por el dorsal; se comparan
  presencia, titularidad, minutos, goles y tarjetas (el acta oficial no recoge asistencias, tiros ni paradas) y se
  listan las líneas oficiales sin ficha para que el cuerpo técnico las añada.

## Vídeos de partido

El cuerpo técnico sube el vídeo del partido entero o en dos partes (`VideoPart`: `Full`, `FirstHalf`, `SecondHalf`;
nunca mezcladas y cada parte una sola vez por plantilla). Solo mp4 y webm (los demás formatos no se reproducen en el
navegador) hasta `Videos:MaxSizeMb` (4 GB). La subida va **por trozos y es reanudable**: `POST
/api/squads/{id}/matches/{matchId}/video/uploads` abre una sesión (`VideoUploadSession`, tabla `VideoUploads`, caduca a las
`Videos:UploadExpiryHours`), los trozos de `Videos:ChunkSizeMb` se envían en orden a `PUT
/api/squads/{id}/video-uploads/{uploadId}/chunks/{index}` (cuerpo binario; cada trozo se escribe desde el offset ya
confirmado, así un reintento nunca corrompe el temporal) y `…/complete` publica el vídeo; `GET …/video-uploads/{uploadId}`
dice por dónde va para reanudar. El `POST …/video` multipart de una sola vez sigue existiendo para archivos pequeños. Se
guarda en disco (`Videos:Directory`, nombre aleatorio; temporales en `uploads/`) con `IVideoStorage`. Acceso
(`VideoAccess`): la plantilla que lo subió (staff vigente y jugadores vinculados) lo ve gratis (`Owner`); los técnicos de
otras plantillas lo ven **previo pago** del precio del vídeo (`Videos:DefaultPriceCents`, `Locked` → `Purchased`; comprar
cualquier parte da acceso a todas las partes del partido de esa plantilla); nadie
más. Como el reproductor del navegador no puede enviar el JWT, `POST /api/videos/{id}/ticket` emite un ticket de corta
vida (`Videos:TicketMinutes`, en memoria) y `GET /api/videos/{id}/stream?ticket=` sirve el archivo con soporte de rangos.
La pasarela de pago es una abstracción (`IPaymentGateway`); la única implementación, `FakePaymentGateway`, aprueba todos
los cobros y los deja en el log: para cobrar de verdad hay que implementar Stripe (u otra) detrás de esa interfaz. La
retransmisión en directo queda para más adelante.

## Configuración y secretos

- `Jwt:Key` (mínimo 32 caracteres) solo está definida en `appsettings.Development.json` para desarrollo.
  Para otros entornos usa user-secrets o variables de entorno:

```bash
dotnet user-secrets set "Jwt:Key" "una-clave-larga-y-aleatoria" --project src/TercerTiempo.Api
```

- `ConnectionStrings:Default` (SQL Server) está vacía en `appsettings.json` a propósito: en desarrollo la da
  `appsettings.Development.json`; en otros entornos, user-secrets o la variable `ConnectionStrings__Default`
  (p. ej. `Server=mi-servidor;Database=TercerTiempo;User Id=tercertiempo;Password=...;Encrypt=True`). La conexión
  reintenta ante errores transitorios y usa consultas divididas para las colecciones.
- `Cors:AllowedOrigins` incluye el frontend de Vite (`http://localhost:5173`).
- `OfficialSheets:*` (asturfutbol.es: URL base, ritmo, códigos de sección, minutos reglamentarios) y `Videos:*`
  (carpeta, tamaño máximo, precio por defecto, formatos, vida de los tickets): ver `appsettings.json`.

## Migraciones y tests

```bash
dotnet ef migrations add NombreDeLaMigracion --project src/TercerTiempo.Infrastructure --startup-project src/TercerTiempo.Api --output-dir Persistence/Migrations
```

```bash
dotnet test
```

(`dotnet-ef` está como herramienta local: `dotnet tool restore` si hace falta.) Con la API en ejecución desde Visual
Studio los DLL de `bin` quedan bloqueados: la configuración `Migrations` del proyecto compila a otra carpeta, por lo
que se puede generar la migración sin parar el depurador (`--configuration Migrations`).

Las migraciones son de SQL Server (`InitialCreate` recrea todo el esquema desde cero). Para empezar de cero basta con
borrar la base de datos: al arrancar se vuelve a crear, migrar y sincronizar.

## Errores: códigos y mensajes en base de datos

Ningún mensaje de error va escrito en el código. Cada excepción lleva un **código** (`ErrorCode`, `Domain/Common/Errors`,
convención `área.motivo`) y sus argumentos; `DomainException`, `NotFoundException`, `ConflictException`,
`ForbiddenException`, `UnauthenticatedException` e `InvalidCredentialsException` derivan de `CodedException`. El
texto sale de la tabla **`ErrorMessages`** (código, idioma, plantilla con `{0}`, `{1}`...), cuya semilla es el
catálogo embebido `ErrorCatalog` (hoy solo español): la configuración de EF la vuelca con `HasData`, así que cada
migración que cambie el catálogo lleva las filas y al crear la base de datos desde cero quedan insertadas.

`GlobalExceptionHandler` resuelve el mensaje con `IErrorMessageResolver` (tabla cacheada 10 minutos) en el idioma de
`Accept-Language` (`es-ES,es;q=0.9` → `es`); si no hay traducción usa el español y, si el código no estuviera en la
tabla, la plantilla embebida. La respuesta ProblemDetails incluye el código en `code`:

```json
{ "status": 422, "title": "Regla de negocio incumplida", "detail": "CD Llanes no se corresponde con el nombre del club (CD Lealtad de Villaviciosa).", "code": "club.team_name_mismatch" }
```

Las reglas de FluentValidation usan `WithCatalogMessage(ErrorCode.X, args)` (código + argumentos + texto por defecto)
y las de contraseña/email de Identity se mapean a `identity.*`; en los errores de validación cada campo lleva el
mensaje ya traducido. Para añadir un idioma: filas nuevas en `ErrorCatalog` (o directamente en la tabla) con el mismo
código y `Language` = "en", y la cultura en `UseRequestLocalization` (Program.cs). El test `ErrorCatalogTests`
comprueba que todo `ErrorCode` tiene mensaje.

## Límite de llamadas y bloqueos

`ClientThrottlingMiddleware` (API) con `ClientThrottle` (Infrastructure/Security) cuenta en memoria las llamadas a
`/api` por IP y por IP + endpoint (método + patrón de ruta) en ventanas de `Throttling:WindowSeconds`. Al superar
`RequestsPerWindow` o `RequestsPerEndpointPerWindow` la IP queda bloqueada `BanMinutes[n]` minutos según su
reincidencia reciente (5 min, 30 min, 1 día) y, pasado el último escalón, el doble cada vez hasta `MaxBanMinutes`;
tras `StrikeMemoryHours` sin incidencias vuelve al primer escalón. Mientras dura el bloqueo responde **429** con
`Retry-After` y ProblemDetails (`throttle.too_many_requests` / `throttle.banned`). Las rutas de `ExemptRoutes` (los
escudos) no cuentan para el límite por endpoint; `TrustForwardedHeaders` solo detrás de un proxy de confianza.
Es por proceso: con varias instancias haría falta compartir los contadores (Redis).

## Decisiones y siguientes pasos sugeridos

- **Identidad separada del dominio**: Identity gestiona credenciales; `Player`, `Coach` y `Club` son los agregados de negocio.
- **El acceso del cuerpo técnico lo da el club**: solo con un nombramiento vigente se crea o edita el acta de una
  plantilla, y nunca más allá de la temporada. Solo hay una cuenta por club, pero queda pendiente verificar que quien
  se da de alta como club es el club (hoy el primero que elige ese nombre se lo queda): aprobación manual o alta por
  invitación.
- **La plantilla es el límite de privacidad**: actas y estadísticas pertenecen a la `Squad`; nada sale de ella
  salvo que su cuerpo técnico la haga pública. El club propietario y los técnicos vigentes generan las invitaciones personales.
- **Roles del staff**: hoy todos los miembros vigentes del cuerpo técnico pueden registrar actas y gestionar la plantilla;
  queda por decidir si algunas acciones (visibilidad, precio de los vídeos) se reservan al primer entrenador.
- **Pagos y vídeo**: pasarela real (Stripe), precio por vídeo o por temporada, reparto con el club que lo sube,
  almacenamiento en blob/CDN y transcodificación; retransmisión en directo.
- **Estadísticas**: comparativa por posición e importar las estadísticas oficiales por jugador que publica SportsPress
  en los eventos jugados para precargar el acta. Los pesos de la nota están en `PerformanceRating` y se pueden afinar
  con datos reales (por ejemplo, contrastándolos con las puntuaciones que publica la web en las convocatorias).
- **Infra**: refresh tokens, despliegue en contenedores, eventos de dominio
  con outbox (ya hay punto de extensión en `AppDbContext.SaveChangesAsync`).
