using System.ComponentModel;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Common;
using TercerTiempo.Domain.Common.Errors;

namespace TercerTiempo.Application.Jobs;

public static class JobNames
{
    public const string FootballData = "football-data";
    public const string OfficialSheets = "official-sheets";
    public const string LicenseRenewals = "license-renewals";
    public const string WeeklySheets = "weekly-sheets";
    public const string LicenseAlerts = "license-alerts";
    public const string MatchOfficials = "match-officials";
    public const string LeagueZones = "league-zones";
    public const string RealDataCopy = "real-data";

    // Lo ejecuta el servicio de vídeo (otro proceso), una ejecución por vídeo subido.
    public const string VideoTranscode = "video-transcode";

    // También del servicio de vídeo: una ejecución por vídeo analizado con TercerTiempo.Analisis.
    public const string VideoAnalysis = "video-analysis";
}

public static class JobRunStatus
{
    public const string Running = "running";
    public const string Succeeded = "succeeded";
    public const string Partial = "partial";
    public const string Failed = "failed";
    public const string Paused = "paused";
    public const string Interrupted = "interrupted";
}

public sealed record JobsOverviewDto(
    IReadOnlyList<JobDto> Jobs,
    IReadOnlyList<ScrapeSourceDto> Sources,
    IReadOnlyList<JobRunDto> Runs,
    IReadOnlyList<ScrapeRequestDto> Requests,
    DateTimeOffset GeneratedAt,
    [property: Description("Quién descarga ahora los datos reales (la base que comparten todos los entornos); null si nadie.")]
    RealDataIngesterDto? Ingester = null);

public sealed record RealDataIngesterDto(
    string Environment,
    string Machine,
    DateTimeOffset Since,
    DateTimeOffset HeartbeatAt,
    [property: Description("Es este mismo proceso.")]
    bool ThisProcess,
    [property: Description("Este entorno puede descargar (RealData:Ingest) cuando el otro pare.")]
    bool ThisEnvironmentCanIngest);

public sealed record JobDto(
    string Name,
    string Title,
    string Description,
    int? IntervalMinutes,
    bool Enabled,
    bool Running,
    DateTimeOffset? NextRunAt,
    JobProgressDto? Progress,
    JobRunDto? LastRun,
    [property: Description("Cuándo toca si va a día y hora fijos («Los lunes a las 7:00»); null si va por intervalo.")]
    string? Schedule = null,
    [property: Description("Solo las actas: la federación está en pausa y el trabajo sigue solo a esa hora; lanzarlo antes lo deja pedido para entonces.")]
    JobPauseDto? Pause = null,
    [property: Description("Descarga de las webs a la base de datos reales: solo la hace el entorno que tiene el bloqueo.")]
    bool RealData = false,
    [property: Description("Lo ejecuta otro proceso (el servicio de vídeo) cuando le llega trabajo: no tiene horario ni se lanza desde aquí.")]
    bool External = false);

public sealed record JobProgressDto(string? Current, int Done, int Total);

public sealed record JobPauseDto(
    DateTimeOffset Until,
    [property: Description("rest (descanso entre tandas), session, retry, blocked, error, hour o day (tope de peticiones); null si no se sabe.")]
    string? Kind,
    string? Reason);

// Un trabajo visto en directo: su estado y lo último que ha tocado en la ejecución en curso o en la última.
public sealed record JobLiveDto(JobDto Job, DateTimeOffset? StartedAt, IReadOnlyList<JobEventDto> Feed);

public static class FederationPause
{
    // La pausa guardada de la web manda; si no la hay, la espera del trabajo viene de sus topes de peticiones.
    public static JobPauseDto? From(ScrapeSourceDto? source, DateTimeOffset? waitingUntil, DateTimeOffset now)
    {
        if (source?.PausedUntil is { } until) return new JobPauseDto(until, source.PauseKind, source.PauseReason);
        if (waitingUntil is not { } waiting || waiting <= now) return null;

        var maxDay = source?.MaxPerDay ?? 0;
        var kind = maxDay > 0 && (source?.DayRequests ?? 0) >= maxDay ? "day"
            : source?.MaxPerHour is { } maxHour && (source.HourRequests ?? 0) >= maxHour ? "hour"
            : null;
        return new JobPauseDto(waiting, kind, null);
    }
}

public sealed record JobRunDto(
    Guid Id,
    string Job,
    bool Manual,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    string? Summary,
    IReadOnlyList<string> Errors,
    int Requests,
    [property: Description("Qué abarcó (datos de las ligas): «Jornada en curso (21 oct – 28 oct)», «Temporada completa»…")]
    string? Scope,
    [property: Description("Entorno que la hizo (en las descargas, el que tenía el bloqueo).")]
    string? Environment = null);

// Contadores del control de peticiones de cada web (hora y día en curso) y lo que se ha llamado en las últimas 24 h.
public sealed record ScrapeSourceDto(
    string Provider,
    int? HourRequests,
    int? MaxPerHour,
    int? DayRequests,
    int? MaxPerDay,
    DateTimeOffset? PausedUntil,
    DateTimeOffset? NextRequestAt,
    int Last24h,
    int Failed24h,
    DateTimeOffset? LastRequestAt,
    [property: Description("Motivo de la pausa: rest (descanso entre tandas), session (la web cerró la sesión, no es un bloqueo), blocked (bloqueo) o error (la web falla).")]
    string? PauseKind = null,
    string? PauseReason = null,
    [property: Description("Peticiones de la tanda en curso; al llegar a MaxPerBurst se descansa BurstRestMinutes y se sigue con una sesión nueva.")]
    int BurstRequests = 0,
    int? MaxPerBurst = null,
    int? BurstRestMinutes = null,
    int? RequestDelayMs = null);

public sealed record ScrapeRequestDto(
    long Id,
    DateTimeOffset At,
    string Provider,
    string Method,
    string Url,
    int? StatusCode,
    int DurationMs,
    string? Error,
    Guid? JobRunId);

public enum JobLaunchResult
{
    Accepted,
    NotFound,
    AlreadyRunning,
    Disabled,
    // Descarga de datos reales y ahora la hace otro entorno.
    Elsewhere,
}

public interface IJobsMonitor
{
    Task<JobsOverviewDto> GetOverviewAsync(string? provider, int requests, CancellationToken cancellationToken);

    // Todos los trabajos con su avance y lo último que han tocado, para «Descargas en directo».
    Task<IReadOnlyList<JobLiveDto>> LiveAsync(CancellationToken cancellationToken);

    // parameters llega tal cual al trabajo (p. ej. el SyncScope de los datos de las ligas); null = lo de su programación.
    JobLaunchResult Launch(string job, object? parameters = null);

    // Contadores y pausa de una web con límite de peticiones (asturfutbol); null si no se conoce.
    Task<ScrapeSourceDto?> SourceAsync(string provider, CancellationToken cancellationToken);

    // Inicio de la última sincronización de la temporada entera que terminó (bien o con algún error).
    Task<DateTimeOffset?> LastFullFootballSyncAsync(CancellationToken cancellationToken);
}

public sealed record GetJobsOverviewQuery(string? Provider = null, int Requests = 200) : IQuery<JobsOverviewDto>;

internal sealed class GetJobsOverviewHandler(IJobsMonitor monitor) : IQueryHandler<GetJobsOverviewQuery, JobsOverviewDto>
{
    public Task<JobsOverviewDto> Handle(GetJobsOverviewQuery query, CancellationToken cancellationToken) =>
        monitor.GetOverviewAsync(string.IsNullOrWhiteSpace(query.Provider) ? null : query.Provider.Trim(), Math.Clamp(query.Requests, 1, 1000), cancellationToken);
}

public sealed record RunJobCommand(string Job) : ICommand<JobDto>;

internal sealed class RunJobHandler(IJobsMonitor monitor) : ICommandHandler<RunJobCommand, JobDto>
{
    public Task<JobDto> Handle(RunJobCommand command, CancellationToken cancellationToken) =>
        JobLauncher.LaunchAsync(monitor, command.Job, null, cancellationToken);
}

internal static class JobLauncher
{
    public static async Task<JobDto> LaunchAsync(IJobsMonitor monitor, string job, object? parameters, CancellationToken cancellationToken)
    {
        switch (monitor.Launch(job, parameters))
        {
            case JobLaunchResult.NotFound: throw new NotFoundException(ErrorCode.JobNotFound, job);
            case JobLaunchResult.AlreadyRunning: throw new ConflictException(ErrorCode.JobAlreadyRunning, job);
            case JobLaunchResult.Disabled: throw new ConflictException(ErrorCode.JobDisabled, job);
            case JobLaunchResult.Elsewhere:
                var ingester = (await monitor.GetOverviewAsync(null, 1, cancellationToken)).Ingester;
                throw new ConflictException(ErrorCode.JobRealDataElsewhere, job, ingester?.Environment ?? "otro");
        }

        var overview = await monitor.GetOverviewAsync(null, 1, cancellationToken);
        return overview.Jobs.Single(j => j.Name == job);
    }
}
