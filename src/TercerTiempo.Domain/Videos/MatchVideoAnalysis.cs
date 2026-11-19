using System.Text.Json;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Common.Errors;

namespace TercerTiempo.Domain.Videos;

// En cola, analizándose, con la propuesta lista, fallido (sin GPU, sin analizador, vídeo ilegible...), cancelado o en
// pausa por el cuerpo técnico. El análisis nunca toca el vídeo ni el acta: solo deja una propuesta que el cuerpo técnico
// revisa y vuelca.
public enum VideoAnalysisStatus
{
    Pending = 1,
    Running = 2,
    Ready = 3,
    Failed = 4,
    Cancelled = 5,
    // Parado por el cuerpo técnico. Si estaba analizándose, el analizador lo sigue teniendo (con su latido) y el proceso
    // espera sin perder lo hecho; si estaba en cola, no se empieza hasta que se reanude.
    Paused = 6,
}

// Una línea del registro del análisis: cada cosa que el analizador va sabiendo («Equipos separados…», «En pausa…»).
public sealed record VideoAnalysisLogEntry(DateTimeOffset At, string Text);

// Lo que deja el analizador al terminar: dónde está su carpeta de salida, con qué versión y pesos trabajó, la propuesta
// (acta-propuesta.json tal cual) y cuánto se fía uno de ella (fotogramas con balón y con el campo calibrado).
public sealed record VideoAnalysisOutcome(
    string OutputPath,
    string AnalyzerVersion,
    string Weights,
    string ProposalJson,
    int Frames,
    decimal BallFramesPct,
    decimal CalibratedFramesPct,
    int AnalyzedSeconds,
    decimal? PitchLengthM = null,
    decimal? PitchWidthM = null,
    decimal? ReadFramesPct = null);

public sealed class MatchVideoAnalysis : AggregateRoot
{
    public const int MaxErrorLength = 1000;
    public const int MaxAssignments = 400;
    public const int MaxWorkerIdLength = 100;
    public const int MaxStageLength = 20;
    public const int MaxLiveLength = 4000;
    public const int MaxLogEntries = 40;
    public const int MaxLogTextLength = 300;

    // Por debajo de esto la propuesta se enseña con un aviso: sin balón a la vista la posesión baila, y sin el campo
    // calibrado no salen ni los tercios ni los eventos de geometría (fueras, tiros, córners).
    public const decimal LowBallFramesPct = 35m;
    public const decimal LowCalibratedFramesPct = 50m;

    // El analizador no pudo leer todos los fotogramas del tramo (archivo dañado o cortado): lo que sale es de menos.
    public const decimal LowReadFramesPct = 95m;

    // El campo del modelo del analizador: con él calibra cuando el club del equipo local no ha apuntado las medidas
    // del suyo.
    public const decimal GenericPitchLengthM = 120m;
    public const decimal GenericPitchWidthM = 70m;

    public Guid VideoId { get; private set; }
    public Guid SquadId { get; private set; }
    public Guid MatchId { get; private set; }

    public VideoAnalysisStatus Status { get; private set; } = VideoAnalysisStatus.Pending;
    public int Progress { get; private set; }
    public int Attempts { get; private set; }
    public string? Error { get; private set; }
    public DateTimeOffset RequestedAt { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? FinishedAt { get; private set; }

    // Quién lo está analizando (equipo:proceso) y cuándo dio señales por última vez: si el proceso muere, el latido
    // caduca y otro analizador (o el mismo al volver) lo devuelve a la cola.
    public string? WorkerId { get; private set; }
    public DateTimeOffset? HeartbeatAt { get; private set; }

    // Lo que está haciendo ahora mismo el analizador: la etapa y el último estado que contó (JSON del servicio de vídeo
    // con el minuto del vídeo, jugadores seguidos, velocidad…), y el registro de lo que ha ido sabiendo.
    public string? Stage { get; private set; }
    public string? LiveJson { get; private set; }
    public DateTimeOffset? LiveAt { get; private set; }
    public string? LogJson { get; private set; }

    // Desde cuándo está en pausa y cuánto ha estado parado en total (no cuenta en lo que tarda).
    public DateTimeOffset? PausedAt { get; private set; }
    public int PausedSeconds { get; private set; }

    public string? OutputPath { get; private set; }
    public string? AnalyzerVersion { get; private set; }
    public string? Weights { get; private set; }
    public string? ProposalJson { get; private set; }
    public int? Frames { get; private set; }
    public decimal? BallFramesPct { get; private set; }
    public decimal? CalibratedFramesPct { get; private set; }
    public int? AnalyzedSeconds { get; private set; }
    public decimal? PitchLengthM { get; private set; }
    public decimal? PitchWidthM { get; private set; }
    public decimal? ReadFramesPct { get; private set; }

    // Lo último que el cuerpo técnico volcó al acta: cuál de los dos equipos detectados es el suyo y a qué ficha
    // corresponde cada jugador seguido (JSON tracklet → ficha), para no repetir la asignación al volver.
    public int? OurTeam { get; private set; }
    public string? AssignmentsJson { get; private set; }
    public DateTimeOffset? AppliedAt { get; private set; }

    public bool IsActive => Status is VideoAnalysisStatus.Pending or VideoAnalysisStatus.Running or VideoAnalysisStatus.Paused;

    // Lo tiene un analizador: analizándose o en pausa a medias (con su proceso esperando).
    public bool IsWorking => Status == VideoAnalysisStatus.Running || (Status == VideoAnalysisStatus.Paused && WorkerId is not null);

    public bool GenericPitch => PitchLengthM == GenericPitchLengthM && PitchWidthM == GenericPitchWidthM;

    public bool LowQuality =>
        Status == VideoAnalysisStatus.Ready
        && (BallFramesPct < LowBallFramesPct || CalibratedFramesPct < LowCalibratedFramesPct || ReadFramesPct < LowReadFramesPct);

    private MatchVideoAnalysis() { }

    // Solo se analiza un vídeo cuyo archivo ya no va a cambiar: comprimido (o dejado tal cual), o con la compresión
    // fallida, que sigue sirviendo el original.
    public static bool CanAnalyze(MatchVideo video) => !video.IsDeleted && video.Status is VideoStatus.Ready or VideoStatus.Failed;

    public static MatchVideoAnalysis Queue(MatchVideo video, DateTimeOffset now)
    {
        EnsureAnalyzable(video);
        return new MatchVideoAnalysis
        {
            Id = NewId(),
            VideoId = video.Id,
            SquadId = video.SquadId,
            MatchId = video.MatchId,
            RequestedAt = now,
        };
    }

    // Volver a analizar: se olvida la propuesta anterior (los jugadores seguidos cambian de número de una pasada a otra).
    public void Restart(MatchVideo video, DateTimeOffset now)
    {
        if (video.Id != VideoId)
            throw new DomainException(ErrorCode.VideoAnalysisNotFound);
        EnsureAnalyzable(video);
        if (IsActive)
            throw new DomainException(ErrorCode.VideoAnalysisAlreadyQueued);

        Status = VideoAnalysisStatus.Pending;
        Progress = 0;
        Attempts = 0;
        Error = null;
        RequestedAt = now;
        StartedAt = null;
        FinishedAt = null;
        PausedAt = null;
        PausedSeconds = 0;
        ClearLive();
        LogJson = null;
        ClearResult();
    }

    public void Start(DateTimeOffset now, string? workerId = null)
    {
        if (Status != VideoAnalysisStatus.Pending)
            throw new DomainException(ErrorCode.VideoAnalysisNotPending);

        Status = VideoAnalysisStatus.Running;
        Attempts++;
        Progress = 0;
        Error = null;
        StartedAt = now;
        FinishedAt = null;
        WorkerId = string.IsNullOrWhiteSpace(workerId) ? null : workerId.Length > MaxWorkerIdLength ? workerId[..MaxWorkerIdLength] : workerId;
        HeartbeatAt = now;
        PausedAt = null;
        PausedSeconds = 0;
        ClearLive();
        LogJson = null;
    }

    public void ReportProgress(int percent)
    {
        EnsureWorking();
        Progress = Math.Clamp(percent, 0, 99);
    }

    // El último estado que contó el analizador (etapa y detalle) y su avance.
    public void ReportLive(int percent, string? stage, string? liveJson, DateTimeOffset now)
    {
        EnsureWorking();
        Progress = Math.Clamp(Math.Max(Progress, percent), 0, 99);
        if (!string.IsNullOrWhiteSpace(stage)) Stage = stage.Length > MaxStageLength ? stage[..MaxStageLength] : stage;
        LiveJson = liveJson is { Length: > 0 and <= MaxLiveLength } ? liveJson : LiveJson;
        LiveAt = now;
    }

    // Una cosa más que se sabe del análisis; se guardan las últimas MaxLogEntries.
    public void AddLog(string text, DateTimeOffset now)
    {
        EnsureWorking();
        var clean = string.Join(' ', (text ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (clean.Length == 0) return;
        var entries = Log.ToList();
        entries.Add(new VideoAnalysisLogEntry(now, clean.Length > MaxLogTextLength ? clean[..MaxLogTextLength] : clean));
        LogJson = JsonSerializer.Serialize(entries.TakeLast(MaxLogEntries).ToList(), LogJsonOptions);
    }

    public IReadOnlyList<VideoAnalysisLogEntry> Log
    {
        get
        {
            if (string.IsNullOrWhiteSpace(LogJson)) return [];
            try
            {
                return JsonSerializer.Deserialize<List<VideoAnalysisLogEntry>>(LogJson, LogJsonOptions) ?? [];
            }
            catch (JsonException)
            {
                return [];
            }
        }
    }

    private static readonly JsonSerializerOptions LogJsonOptions = new(JsonSerializerDefaults.Web);

    public void Beat(DateTimeOffset now)
    {
        EnsureWorking();
        HeartbeatAt = now;
    }

    // Parar sin perder nada: lo que está en cola no se empieza y lo que se está analizando espera donde va.
    public void Pause(DateTimeOffset now)
    {
        if (Status is not (VideoAnalysisStatus.Pending or VideoAnalysisStatus.Running))
            throw new DomainException(ErrorCode.VideoAnalysisNotPausable);
        Status = VideoAnalysisStatus.Paused;
        PausedAt = now;
    }

    // Sigue donde se quedó: si lo tenía un analizador, vuelve a analizarse; si no, vuelve a la cola.
    public void Resume(DateTimeOffset now)
    {
        if (Status != VideoAnalysisStatus.Paused)
            throw new DomainException(ErrorCode.VideoAnalysisNotPaused);
        if (PausedAt is { } since && now > since)
            PausedSeconds += (int)Math.Round((now - since).TotalSeconds);
        PausedAt = null;
        Status = WorkerId is null ? VideoAnalysisStatus.Pending : VideoAnalysisStatus.Running;
    }

    // Lleva más de `lease` sin dar señales: el proceso que lo analizaba se cerró, se colgó o perdió la base.
    public bool LeaseExpired(DateTimeOffset now, TimeSpan lease) =>
        IsWorking && (HeartbeatAt ?? StartedAt ?? RequestedAt) < now - lease;

    public void Complete(VideoAnalysisOutcome outcome, DateTimeOffset now)
    {
        EnsureWorking();
        if (string.IsNullOrWhiteSpace(outcome.ProposalJson))
            throw new DomainException(ErrorCode.VideoAnalysisEmptyProposal);

        OutputPath = Guard.NotEmpty(outcome.OutputPath, "carpeta de salida", 500);
        AnalyzerVersion = Guard.NotEmpty(outcome.AnalyzerVersion, "versión del analizador", 40);
        Weights = Guard.NotEmpty(outcome.Weights, "pesos", 500);
        ProposalJson = outcome.ProposalJson;
        Frames = Guard.NotNegative(outcome.Frames, "fotogramas");
        BallFramesPct = Guard.InRange(outcome.BallFramesPct, 0m, 100m, "fotogramas con balón");
        CalibratedFramesPct = Guard.InRange(outcome.CalibratedFramesPct, 0m, 100m, "fotogramas calibrados");
        AnalyzedSeconds = Guard.NotNegative(outcome.AnalyzedSeconds, "segundos analizados");
        PitchLengthM = outcome.PitchLengthM;
        PitchWidthM = outcome.PitchWidthM;
        ReadFramesPct = outcome.ReadFramesPct is { } read ? Guard.InRange(read, 0m, 100m, "fotogramas leídos") : null;
        OurTeam = null;
        AssignmentsJson = null;
        AppliedAt = null;

        Status = VideoAnalysisStatus.Ready;
        Progress = 100;
        Error = null;
        FinishedAt = now;
        HeartbeatAt = null;
        EndPause(now);
        ClearLive();
    }

    public void Fail(string error, DateTimeOffset now)
    {
        EnsureWorking();
        Status = VideoAnalysisStatus.Failed;
        Error = ErrorText(error);
        FinishedAt = now;
        HeartbeatAt = null;
        EndPause(now);
        ClearResult();
    }

    // Un fallo pasajero (la base, el disco, la red): vuelve a la cola con el intento contado y el motivo a la vista.
    public void RetryLater(string error)
    {
        EnsureWorking();
        BackToQueue();
        Error = ErrorText(error);
    }

    // El proceso que lo analizaba se paró a medias (apagado, reinicio): vuelve a la cola sin contar el intento. Si
    // estaba en pausa, sigue en pausa (empezará de nuevo cuando se reanude).
    public void Requeue()
    {
        if (!IsWorking) return;
        BackToQueue();
        Attempts = Math.Max(0, Attempts - 1);
    }

    // El proceso que lo analizaba murió sin avisar (se cerró la ventana, se colgó el equipo): el intento cuenta, por si
    // es el propio vídeo el que lo tumba, y agotados los intentos queda fallido.
    public void Recover(int maxAttempts, DateTimeOffset now)
    {
        if (!IsWorking) return;
        if (Attempts >= maxAttempts)
        {
            Fail("El analizador se paró a medias (se cerró o dejó de responder) y ya no quedan reintentos: vuelve a pedir el análisis.", now);
            return;
        }
        BackToQueue();
        Error = "El analizador anterior se paró a medias: se vuelve a intentar.";
    }

    public void Cancel(DateTimeOffset now)
    {
        if (!IsActive)
            throw new DomainException(ErrorCode.VideoAnalysisNotActive);

        Status = VideoAnalysisStatus.Cancelled;
        Progress = 0;
        Error = null;
        FinishedAt = now;
        WorkerId = null;
        HeartbeatAt = null;
        EndPause(now);
        ClearLive();
    }

    public void MarkApplied(int ourTeam, string assignmentsJson, DateTimeOffset now)
    {
        if (Status != VideoAnalysisStatus.Ready)
            throw new DomainException(ErrorCode.VideoAnalysisNotReady);
        if (ourTeam is not (0 or 1))
            throw new DomainException(ErrorCode.VideoAnalysisTeamInvalid);

        OurTeam = ourTeam;
        AssignmentsJson = assignmentsJson;
        AppliedAt = now;
    }

    private void BackToQueue()
    {
        Status = Status == VideoAnalysisStatus.Paused ? VideoAnalysisStatus.Paused : VideoAnalysisStatus.Pending;
        Progress = 0;
        StartedAt = null;
        WorkerId = null;
        HeartbeatAt = null;
        ClearLive();
    }

    private void EndPause(DateTimeOffset now)
    {
        if (PausedAt is { } since && now > since)
            PausedSeconds += (int)Math.Round((now - since).TotalSeconds);
        PausedAt = null;
    }

    private void ClearLive()
    {
        Stage = null;
        LiveJson = null;
        LiveAt = null;
    }

    private static string ErrorText(string error) =>
        string.IsNullOrWhiteSpace(error) ? "Error desconocido" : error.Length > MaxErrorLength ? error[..MaxErrorLength] : error;

    private void ClearResult()
    {
        OutputPath = null;
        AnalyzerVersion = null;
        Weights = null;
        ProposalJson = null;
        Frames = null;
        BallFramesPct = null;
        CalibratedFramesPct = null;
        AnalyzedSeconds = null;
        PitchLengthM = null;
        PitchWidthM = null;
        ReadFramesPct = null;
        OurTeam = null;
        AssignmentsJson = null;
        AppliedAt = null;
    }

    private void EnsureWorking()
    {
        if (!IsWorking)
            throw new DomainException(ErrorCode.VideoAnalysisNotRunning);
    }

    private static void EnsureAnalyzable(MatchVideo video)
    {
        if (video.IsDeleted)
            throw new DomainException(ErrorCode.VideoAlreadyDeleted);
        if (!CanAnalyze(video))
            throw new DomainException(ErrorCode.VideoAnalysisVideoNotReady);
    }
}

// El mejor recorte de cada jugador seguido, para que el cuerpo técnico lo reconozca y lo asigne a una ficha. Se guardan
// en la base (son JPEG de un par de KB): así la API los sirve sin depender del disco del servicio de vídeo.
public sealed class VideoAnalysisCrop
{
    public Guid AnalysisId { get; private set; }
    public int Tracklet { get; private set; }
    public byte[] Content { get; private set; } = [];

    private VideoAnalysisCrop() { }

    public VideoAnalysisCrop(Guid analysisId, int tracklet, byte[] content)
    {
        AnalysisId = Guard.NotEmpty(analysisId, nameof(analysisId));
        Tracklet = Guard.NotNegative(tracklet, "jugador seguido");
        Content = content is { Length: > 0 } ? content : throw new DomainException(ErrorCode.FileEmpty);
    }
}

public interface IMatchVideoAnalysisStore
{
    Task<MatchVideoAnalysis?> GetByVideoAsync(Guid videoId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MatchVideoAnalysis>> GetByVideosAsync(IEnumerable<Guid> videoIds, CancellationToken cancellationToken = default);
    void Add(MatchVideoAnalysis analysis);
    void Remove(MatchVideoAnalysis analysis);

    // Cola de análisis: el pedido hace más tiempo que siga esperando, de un vídeo que no se haya eliminado.
    Task<MatchVideoAnalysis?> GetNextPendingAsync(int maxAttempts, CancellationToken cancellationToken = default);
    // Los que tiene algún analizador: analizándose o en pausa a medias.
    Task<IReadOnlyList<MatchVideoAnalysis>> GetRunningAsync(CancellationToken cancellationToken = default);

    // Cómo está la fila en la base ahora mismo (sin seguirla): el analizador lo mira cada pocos segundos para enterarse
    // de una pausa, una reanudación o una cancelación. null si ya no existe.
    Task<VideoAnalysisControlState?> GetControlAsync(Guid analysisId, CancellationToken cancellationToken = default);

    // Guarda el análisis terminado con sus recortes en una sola transacción (los de la pasada anterior se van a la vez).
    Task SaveResultAsync(Guid analysisId, IReadOnlyCollection<VideoAnalysisCrop> crops, CancellationToken cancellationToken = default);

    // Olvida lo que no se pudo guardar y vuelve a leer la fila tal como está en la base; false si ya no existe.
    Task<bool> DiscardChangesAsync(MatchVideoAnalysis analysis, CancellationToken cancellationToken = default);
    Task<byte[]?> GetCropAsync(Guid analysisId, int tracklet, CancellationToken cancellationToken = default);
    Task<IReadOnlySet<int>> GetCropTrackletsAsync(Guid analysisId, CancellationToken cancellationToken = default);
}

public sealed record VideoAnalysisControlState(VideoAnalysisStatus Status, string? WorkerId);

// Latido de cada analizador en marcha (el servicio de vídeo con Analysis:RunWorker): así la app sabe si hay alguien que
// atienda la cola o si un análisis se va a quedar «en cola» para siempre.
public sealed class VideoAnalyzerHeartbeat
{
    public const int MaxWorkerIdLength = MatchVideoAnalysis.MaxWorkerIdLength;
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan OnlineWindow = TimeSpan.FromMinutes(2);

    public string WorkerId { get; private set; } = "";
    public DateTimeOffset StartedAt { get; private set; }
    public DateTimeOffset SeenAt { get; private set; }

    private VideoAnalyzerHeartbeat() { }

    public VideoAnalyzerHeartbeat(string workerId, DateTimeOffset startedAt, DateTimeOffset now)
    {
        WorkerId = Guard.NotEmpty(workerId, "analizador", MaxWorkerIdLength);
        StartedAt = startedAt;
        SeenAt = now;
    }

    public static bool IsOnline(DateTimeOffset? lastSeen, DateTimeOffset now) => lastSeen is { } seen && now - seen < OnlineWindow;
}

public interface IVideoAnalyzerHeartbeatStore
{
    Task BeatAsync(string workerId, DateTimeOffset startedAt, DateTimeOffset now, CancellationToken cancellationToken = default);
    Task<DateTimeOffset?> LastSeenAsync(CancellationToken cancellationToken = default);
    Task RemoveAsync(string workerId, CancellationToken cancellationToken = default);
}
