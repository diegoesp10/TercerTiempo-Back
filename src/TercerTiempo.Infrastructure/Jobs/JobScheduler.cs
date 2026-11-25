using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TercerTiempo.Application.Abstractions.OfficialSheets;
using TercerTiempo.Application.Jobs;
using TercerTiempo.Application.Licensing;
using TercerTiempo.Infrastructure.FootballData;
using TercerTiempo.Infrastructure.FootballData.FutbolAsturiano;
using TercerTiempo.Infrastructure.OfficialSheets.Asturfutbol;
using TercerTiempo.Infrastructure.Persistence;
using TercerTiempo.Infrastructure.RealData;

namespace TercerTiempo.Infrastructure.Jobs;

// RealData: descarga de las webs a la base de datos reales; solo la ejecuta el entorno que tiene el bloqueo
// (RealDataLease) y su historial vive en esa base.
// External: lo ejecuta otro proceso (el servicio de vídeo) cuando le llega trabajo; aquí solo se enseña su historial.
internal sealed record JobDefinition(
    string Name, string Title, string Description, TimeSpan? Interval, TimeSpan FirstRunDelay, bool Enabled, WeeklySlot? Weekly = null,
    bool RealData = false, bool External = false);

// Un día y una hora fijos de cada semana, en la hora de la federación.
internal sealed record WeeklySlot(DayOfWeek Day, TimeOnly At, TimeZoneInfo Zone)
{
    private static readonly CultureInfo Spanish = CultureInfo.GetCultureInfo("es-ES");

    public DateTimeOffset Next(DateTimeOffset after)
    {
        var local = TimeZoneInfo.ConvertTime(after, Zone).DateTime;
        var candidate = local.Date.AddDays(((int)Day - (int)local.DayOfWeek + 7) % 7) + At.ToTimeSpan();
        if (candidate <= local) candidate = candidate.AddDays(7);
        return SiteClock.ToUtc(candidate, Zone);
    }

    public string Describe() => $"Los {Spanish.DateTimeFormat.GetDayName(Day)} a las {At.ToString("H:mm", Spanish)}";
}

// Quiet: no había nada que hacer y la ejecución no se guarda en el historial (la copia de los datos reales sin cambios).
internal sealed record JobOutcome(string Summary, IReadOnlyList<string> Errors, bool Quiet = false);

internal sealed record JobScope(string Kind, string Label);

// Ejecución en curso: las llamadas a webs externas hechas dentro de ella quedan apuntadas a su nombre.
internal static class JobContext
{
    private static readonly AsyncLocal<Guid?> Current = new();

    public static Guid? RunId
    {
        get => Current.Value;
        set => Current.Value = value;
    }
}

// Cuándo toca cada trabajo en segundo plano. La hora de la última ejecución se lee de la base de datos, así que
// reiniciar la API no vuelve a lanzar los scrapeos: se espera a que pase el intervalo desde la anterior.
internal sealed class JobScheduler
{
    private static readonly TimeSpan RequestRetention = TimeSpan.FromDays(30);
    private static readonly TimeSpan RunRetention = TimeSpan.FromDays(180);

    private readonly Lock _lock = new();
    private readonly Dictionary<string, Slot> _slots;
    private readonly IServiceScopeFactory _scopes;
    private readonly IJobProgress _progress;
    private readonly IRealDataLease _lease;
    private readonly string _environment;
    private readonly TimeProvider _clock;
    private readonly ILogger<JobScheduler> _logger;

    public JobScheduler(
        IOptions<FootballDataOptions> football,
        IOptions<OfficialSheetOptions> sheets,
        IOptions<LicensingOptions> licensing,
        IOptions<RealDataOptions> realData,
        IOptions<TercerTiempo.Application.Abstractions.Videos.VideoAnalysisOptions> analysis,
        IOptions<TercerTiempo.Infrastructure.Grounds.GroundOptions> grounds,
        IRealDataLease lease,
        IHostEnvironment environment,
        IServiceScopeFactory scopes,
        IJobProgress progress,
        TimeProvider clock,
        ILogger<JobScheduler> logger)
    {
        _scopes = scopes;
        _progress = progress;
        _lease = lease;
        _environment = environment.EnvironmentName;
        _clock = clock;
        _logger = logger;

        var footballMinutes = football.Value.SyncIntervalMinutes;
        var sheetMinutes = sheets.Value.BackgroundScanMinutes;
        var ingest = lease.CanIngest;
        var weeklyDays = sheets.Value.WeeklyQueueDays;
        JobDefinition[] definitions =
        [
            new(JobNames.FootballData,
                "Datos de las ligas",
                $"Ligas, equipos, partidos, resultados, clasificaciones y plantillas desde el proveedor de datos ({football.Value.Provider}).",
                footballMinutes > 0 ? TimeSpan.FromMinutes(footballMinutes) : null,
                TimeSpan.FromSeconds(10),
                ingest,
                RealData: true),
            new(JobNames.OfficialSheets,
                "Actas públicas",
                $"Busca las actas que faltan o toca refrescar (hasta {sheets.Value.BackgroundBatch} por pasada) en asturfutbol.es, con su límite de peticiones.",
                sheetMinutes > 0 ? TimeSpan.FromMinutes(sheetMinutes) : null,
                TimeSpan.FromSeconds(Math.Max(0, sheets.Value.BackgroundStartDelaySeconds)),
                ingest && sheets.Value.Enabled,
                RealData: true),
            new(JobNames.LicenseRenewals,
                "Renovaciones de licencias",
                "Cobra (sin cobro real) el periodo siguiente de las suscripciones de licencia que llegan a su día de renovación. Solo con la pasarela simulada; con Stripe las renueva su webhook.",
                TimeSpan.FromMinutes(1440),
                TimeSpan.FromSeconds(60),
                licensing.Value.Provider == "mock"),
            new(JobNames.WeeklySheets,
                "Actas del fin de semana",
                $"Pone en cola las actas de los partidos de los últimos {weeklyDays} días de las ligas de la federación asturiana (regionales y 3ª RFEF); se descargan en tandas con su límite de peticiones.",
                weeklyDays > 0 && sheetMinutes > 0 ? TimeSpan.FromDays(7) : null,
                TimeSpan.FromSeconds(30),
                ingest && sheets.Value.Enabled && weeklyDays > 0,
                sheetMinutes > 0 ? new WeeklySlot(sheets.Value.WeeklyQueueDay, sheets.Value.WeeklyQueueAt, SiteClock.Resolve(sheets.Value.Asturfutbol.TimeZone)) : null,
                RealData: true),
            new(JobNames.MatchOfficials,
                "Árbitros y cuerpos técnicos",
                "Guarda el equipo arbitral de cada partido y el cuerpo técnico de cada equipo que figura en el acta, con las tarjetas que le enseñó el árbitro: el de las actas de la federación ya descargadas (sin peticiones nuevas) y, para los jugados que sigan sin él, el que publique futbolasturiano.es en la página del partido (hasta 60 por pasada; se vuelve a mirar a los tres días, como mucho tres veces).",
                footballMinutes > 0 ? TimeSpan.FromMinutes(Math.Max(footballMinutes, 60)) : null,
                TimeSpan.FromMinutes(2),
                ingest,
                RealData: true),
            new(JobNames.LeagueZones,
                "Plazas de las ligas",
                "Lee en futbolasturiano.es qué puestos de cada liga son de ascenso directo, promoción de ascenso, promoción por la permanencia o descenso (los colores de su clasificación), para la clasificación y el previsor de resultados. Una página por liga y solo se guarda lo que cambia.",
                footballMinutes > 0 ? TimeSpan.FromDays(1) : null,
                TimeSpan.FromMinutes(3),
                ingest,
                RealData: true),
            new(JobNames.Grounds,
                "Campos de fútbol",
                $"Da de alta en el catálogo los campos nuevos que traen los partidos y busca en OpenStreetMap la dirección y el punto exacto de los que aún no lo tienen (hasta {TercerTiempo.Application.Grounds.SyncGroundsCommand.LocatePerRun} por pasada, una petición por segundo). Solo cuenta un campo o recinto deportivo con ese nombre en Asturias; si no aparece se vuelve a mirar al mes, como mucho tres veces, y el mapa se abre buscando el campo por su nombre.",
                footballMinutes > 0 ? TimeSpan.FromDays(1) : null,
                TimeSpan.FromMinutes(4),
                ingest && grounds.Value.Enabled,
                RealData: true),
            new(JobNames.CommitteeSanctions,
                "Sanciones del comité",
                "Lee en asturfutbol.es la página de sanciones de cada grupo de la federación (una petición por grupo con toda la temporada, con su límite de peticiones) para saber cuántos partidos de suspensión pone el comité de competición a los jugadores y al cuerpo técnico (a este, también los días de inhabilitación); lo que publica manda sobre lo que dicen las tarjetas. Un grupo solo se pide si tiene una jornada jugada que el comité aún no ha publicado (desde dos días después de su último partido y como mucho cada 12 horas) y, si no, una vez por semana por si corrige algo.",
                sheetMinutes > 0 ? TimeSpan.FromHours(3) : null,
                TimeSpan.FromMinutes(5),
                ingest && sheets.Value.Enabled,
                RealData: true),
            new(JobNames.SuspensionAlerts,
                "Avisos de sanciones",
                "Avisa en la campanita al jugador, a su cuerpo técnico y a su club cuando el comité de competición le pone partidos de suspensión («Te han caído 2 partidos de suspensión», con los partidos que se pierde), y da por resueltos los avisos cuando ya los ha cumplido o el comité los cambia. Lee la base de este entorno, sin peticiones a ninguna web; también se lanza en cuanto llegan sanciones nuevas con los datos reales.",
                TimeSpan.FromHours(1),
                TimeSpan.FromSeconds(50),
                true),
            new(JobNames.MatchReports,
                "Crónicas de los partidos",
                "Lee en futbolasturiano.es la crónica de los partidos de los últimos siete días que ya tienen el acta oficial cerrada (de 20 en 20, en una sola petición) para encontrar a los jugadores que se lesionaron, que el acta de la federación no dice: la frase tiene que contar la lesión y nombrar a alguien que jugó, y el minuto es el de su salida en el acta. Cada partido se lee en cuanto tiene el acta cerrada y, si ya hay crónica, una vez más al día siguiente por si la completan; sin crónica, cada seis horas, como mucho seis veces. De la crónica solo se guardan las frases de las lesiones.",
                footballMinutes > 0 ? TimeSpan.FromHours(1) : null,
                TimeSpan.FromMinutes(4),
                ingest,
                RealData: true),
            new(JobNames.InjuryProposals,
                "Posibles lesiones",
                "Busca en las plantillas de este entorno a los jugadores que las crónicas dan por lesionados (por su línea del acta oficial) y se los propone al club y al cuerpo técnico con un aviso en la campanita, para que registren la lesión con el formulario ya relleno o la descarten; nada cuenta como lesión hasta que se registra. No se propone si ya tiene una lesión abierta o un parte de esos días. Lee la base de este entorno, sin peticiones a ninguna web; también se lanza en cuanto llegan lesiones nuevas con los datos reales.",
                TimeSpan.FromHours(1),
                TimeSpan.FromSeconds(65),
                true),
            new(JobNames.CallUpReminders,
                "Avisos de convocatoria",
                "Recuerda en la campanita al club y al cuerpo técnico de cada equipo, el día antes del partido, que la convocatoria de su próximo partido sigue sin enviar (sin hacer o en borrador). Se da por resuelto al enviarla o al empezar el partido, cuando ya no se puede hacer. Lee la base de este entorno, sin peticiones a ninguna web.",
                TimeSpan.FromHours(1),
                TimeSpan.FromSeconds(70),
                true),
            new(JobNames.InjuryCarryOver,
                "Lesiones a la temporada nueva",
                "Pasa a la plantilla de la temporada nueva las lesiones que seguían abiertas al acabar la anterior, si la misma persona está en una plantilla del club (con cuenta, en cualquier equipo; sin ella, por su nombre en el mismo equipo y solo si no hay dudas): no sale disponible y los días de baja se siguen contando desde el parte. Solo cuando la temporada nueva ya ha empezado o es la actual. Se hace también al abrir la plantilla, al importar de la federación y al añadir un jugador; esto recoge lo que llega después (una cuenta vinculada, un jugador reincorporado). Lee la base de este entorno, sin peticiones a ninguna web.",
                TimeSpan.FromHours(1),
                TimeSpan.FromSeconds(55),
                true),
            new(JobNames.LicenseAlerts,
                "Avisos de caducidad",
                "Avisa en la campanita de cada club cuando su licencia va a acabar sin renovarse (la prueba sin contratar o la renovación cancelada), con los días de antelación de la administración, y da por resueltos los que ya no aplican.",
                TimeSpan.FromHours(6),
                TimeSpan.FromSeconds(45),
                true),
            new(JobNames.RealDataCopy,
                "Datos reales",
                "Trae a la base de este entorno lo nuevo de la base de datos reales que comparten todos los entornos (ligas, partidos, resultados, plantillas publicadas, árbitros y actas oficiales), sin peticiones a ninguna web. Respeta lo de cada club: su escudo y las fichas que gestiona.",
                TimeSpan.FromMinutes(Math.Max(1, realData.Value.CopyMinutes)),
                TimeSpan.FromSeconds(5),
                true),
            new(JobNames.VideoTranscode,
                "Compresión de vídeos",
                "El servicio de vídeo recomprime cada vídeo subido a H.264 sin bajar de 1080p (calidad constante CRF 22) para que ocupe la mitad o menos; mientras tanto se sirve el original. Una ejecución por vídeo, al subirlo.",
                null,
                TimeSpan.Zero,
                true,
                External: true),
            new(JobNames.VideoAnalysis,
                "Análisis de vídeos",
                "El servicio de vídeo pasa cada vídeo ya comprimido por el analizador (TercerTiempo.Analisis, con GPU): sigue a los jugadores y al balón y deja una propuesta de acta interna (posesión, tiros, córners, saques y acciones por jugador) que el cuerpo técnico revisa y vuelca. Una ejecución por vídeo, al terminar de comprimirse o al pedirlo desde el acta.",
                null,
                TimeSpan.Zero,
                analysis.Value.Enabled,
                External: true),
        ];
        _slots = definitions.ToDictionary(d => d.Name, d => new Slot(d));
    }

    public IEnumerable<JobDefinition> Definitions => _slots.Values.Select(s => s.Definition);

    public JobDefinition Get(string job) => _slots[job].Definition;

    public (bool Running, DateTimeOffset? NextRunAt) State(string job)
    {
        var slot = _slots[job];
        lock (_lock) return (slot.Running, slot.Running ? null : slot.NextRunAt);
    }

    public JobLaunchResult Launch(string job, object? parameters = null)
    {
        if (!_slots.TryGetValue(job, out var slot)) return JobLaunchResult.NotFound;
        if (!slot.Definition.Enabled || slot.Definition.External) return JobLaunchResult.Disabled;
        if (slot.Definition.RealData && !_lease.IsHeld) return JobLaunchResult.Elsewhere;

        lock (_lock)
        {
            if (slot.Running) return JobLaunchResult.AlreadyRunning;
            slot.Triggered = true;
            slot.Parameters = parameters;
            slot.Signal.TrySetResult();
        }
        return JobLaunchResult.Accepted;
    }

    // Lo que se pidió al lanzarlo a mano (y se olvida al leerlo); null si fue a su hora o sin indicar nada.
    public object? TakeParameters(string job)
    {
        var slot = _slots[job];
        lock (_lock)
        {
            var parameters = slot.Parameters;
            slot.Parameters = null;
            return parameters;
        }
    }

    public Task WhenTriggered(string job)
    {
        lock (_lock) return _slots[job].Signal.Task;
    }

    public bool TryConsumeTrigger(string job)
    {
        var slot = _slots[job];
        lock (_lock)
        {
            if (!slot.Triggered) return false;
            slot.Triggered = false;
            slot.Signal = new(TaskCreationOptions.RunContinuationsAsynchronously);
            return true;
        }
    }

    public void ScheduleNext(string job, DateTimeOffset? at)
    {
        var slot = _slots[job];
        lock (_lock) slot.NextRunAt = at;
    }

    // Primera ejecución tras arrancar: un intervalo después de la última guardada (o enseguida si nunca se ha hecho).
    // Las que quedaron en marcha porque la API se paró a medias se cierran como interrumpidas. Si la base no responde
    // (arranca a la vez, un corte de red), se reintenta con esperas crecientes: un fallo aquí saldría del trabajo y, con
    // él, pararía la API entera.
    public async Task<DateTimeOffset?> FirstDueAsync(string job, CancellationToken cancellationToken)
    {
        var wait = TimeSpan.FromSeconds(5);
        while (true)
        {
            try
            {
                return await FirstDueOnceAsync(job, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "{Job}: no se pudo leer la última ejecución; se reintenta en {Seconds} s", job, (int)wait.TotalSeconds);
                await Task.Delay(wait, _clock, cancellationToken);
                wait = TimeSpan.FromSeconds(Math.Min(wait.TotalSeconds * 2, 300));
            }
        }
    }

    private async Task<DateTimeOffset?> FirstDueOnceAsync(string job, CancellationToken cancellationToken)
    {
        var definition = Get(job);
        await WhenCanRunAsync(job, cancellationToken);
        var now = _clock.GetUtcNow();

        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Set<JobRun>()
            .Where(r => r.Job == job && r.Status == JobRunStatus.Running)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, JobRunStatus.Interrupted), cancellationToken);

        if (definition.Interval is not { } interval) return null;

        var lastStart = await db.Set<JobRun>()
            .Where(r => r.Job == job)
            .OrderByDescending(r => r.StartedAt)
            .Select(r => (DateTimeOffset?)r.StartedAt)
            .FirstOrDefaultAsync(cancellationToken);

        var earliest = now + definition.FirstRunDelay;
        // A día fijo: el siguiente tras la última ejecución; si se pasó con la API apagada, se hace al arrancar.
        if (definition.Weekly is { } weekly)
        {
            var next = weekly.Next(lastStart ?? now);
            return next > earliest ? next : earliest;
        }
        if (lastStart is null) return earliest;
        var due = lastStart.Value + interval;
        return due > earliest ? due : earliest;
    }

    // Las descargas de datos reales esperan a que este proceso tenga el bloqueo (si otro entorno está descargando, hasta que
    // pare); los demás trabajos, nada.
    public Task WhenCanRunAsync(string job, CancellationToken cancellationToken) =>
        Get(job).RealData ? _lease.WhenHeldAsync(cancellationToken) : Task.CompletedTask;

    // Espera a la hora prevista o a que se lance a mano desde /jobs; devuelve si ha sido a mano.
    public async Task<bool> WaitAsync(string job, DateTimeOffset? due, CancellationToken cancellationToken)
    {
        ScheduleNext(job, due);
        while (true)
        {
            if (TryConsumeTrigger(job)) return true;

            var signal = WhenTriggered(job);
            if (due is null)
            {
                await signal.WaitAsync(cancellationToken);
                continue;
            }

            var wait = due.Value - _clock.GetUtcNow();
            if (wait <= TimeSpan.Zero) return false;

            using var delay = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            await Task.WhenAny(signal, Task.Delay(WallClockDelay.Step(wait), _clock, delay.Token));
            await delay.CancelAsync();
            cancellationToken.ThrowIfCancellationRequested();
        }
    }

    public async Task RunAsync(
        string job, bool manual, Func<CancellationToken, Task<JobOutcome>> work, CancellationToken cancellationToken, JobScope? scope = null)
    {
        var slot = _slots[job];
        if (slot.Definition.RealData && !_lease.IsHeld)
        {
            _logger.LogInformation("{Job}: no se ejecuta, los datos reales los descarga otro entorno", job);
            return;
        }

        lock (_lock) slot.Running = true;
        _progress.Start(job);

        var run = new JobRun
        {
            Id = Guid.CreateVersion7(),
            Job = job,
            Manual = manual,
            Status = JobRunStatus.Running,
            StartedAt = _clock.GetUtcNow(),
            ScopeKind = scope?.Kind,
            ScopeLabel = scope?.Label,
            Environment = _environment,
        };

        var status = JobRunStatus.Succeeded;
        string? summary = null;
        IReadOnlyList<string> errors = [];
        var quiet = false;
        var leaseLost = slot.Definition.RealData ? _lease.HeldToken : CancellationToken.None;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, leaseLost);
        try
        {
            await SaveAsync(db => db.Add(run));
            JobContext.RunId = run.Id;

            var outcome = await work(linked.Token);
            summary = outcome.Summary;
            errors = outcome.Errors;
            status = errors.Count == 0 ? JobRunStatus.Succeeded : JobRunStatus.Partial;
            quiet = outcome.Quiet && errors.Count == 0;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            status = JobRunStatus.Interrupted;
            summary = "Parada al apagar la API";
            throw;
        }
        catch (OperationCanceledException) when (leaseLost.IsCancellationRequested)
        {
            status = JobRunStatus.Interrupted;
            summary = "Parada: este entorno ha perdido el bloqueo de los datos reales y puede descargarlos otro";
        }
        catch (ProviderThrottledException ex)
        {
            status = JobRunStatus.Paused;
            summary = ex.Message;
            throw;
        }
        catch (Exception ex)
        {
            status = JobRunStatus.Failed;
            errors = [ex.Message];
            throw;
        }
        finally
        {
            JobContext.RunId = null;
            // Primero se guarda el resultado: si no, durante un instante el trabajo ya no figura en marcha pero su
            // última ejecución sí.
            await FinishAsync(run.Id, status, summary, errors, quiet);
            lock (_lock) slot.Running = false;
            _progress.Finish(job);

            // Lo que acaba de bajar a la base de datos reales llega enseguida a la de este entorno.
            if (slot.Definition.RealData) Launch(JobNames.RealDataCopy);
        }
    }

    // Cada ejecución se cierra en la base de su trabajo: las descargas en la de los datos reales (junto con su registro de
    // llamadas), los demás en la del entorno.
    private async Task FinishAsync(Guid runId, string status, string? summary, IReadOnlyList<string> errors, bool quiet)
    {
        try
        {
            using var scope = _scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var now = _clock.GetUtcNow();

            var run = await db.Set<JobRun>().SingleOrDefaultAsync(r => r.Id == runId);
            if (run is not null && quiet)
            {
                db.Remove(run);
                await db.SaveChangesAsync();
            }
            else if (run is not null)
            {
                run.Status = status;
                run.FinishedAt = now;
                run.Summary = summary is { Length: > 1000 } ? summary[..1000] : summary;
                run.Errors = errors.Count == 0 ? null : string.Join('\n', errors);
                run.Requests = RealDataAmbient.Active ? await db.Set<ScrapeRequest>().CountAsync(r => r.JobRunId == runId) : 0;
                await db.SaveChangesAsync();
            }

            var runCutoff = now - RunRetention;
            await db.Set<JobRun>().Where(r => r.StartedAt < runCutoff).ExecuteDeleteAsync();
            if (RealDataAmbient.Active)
            {
                var requestCutoff = now - RequestRetention;
                await db.Set<ScrapeRequest>().Where(r => r.At < requestCutoff).ExecuteDeleteAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo guardar el final de la ejecución {RunId}", runId);
        }
    }

    private async Task SaveAsync(Action<AppDbContext> change)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        change(db);
        await db.SaveChangesAsync();
    }

    private sealed class Slot(JobDefinition definition)
    {
        public JobDefinition Definition { get; } = definition;
        public bool Running;
        public bool Triggered;
        public object? Parameters;
        public DateTimeOffset? NextRunAt;
        public TaskCompletionSource Signal = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
