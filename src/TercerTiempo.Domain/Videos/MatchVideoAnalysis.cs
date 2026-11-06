using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Common.Errors;

namespace TercerTiempo.Domain.Videos;

// En cola, analizándose, con la propuesta lista o fallido (sin GPU, sin analizador, vídeo ilegible...). El análisis
// nunca toca el vídeo ni el acta: solo deja una propuesta que el cuerpo técnico revisa y vuelca.
public enum VideoAnalysisStatus
{
    Pending = 1,
    Running = 2,
    Ready = 3,
    Failed = 4,
}

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
    decimal? PitchWidthM = null);

public sealed class MatchVideoAnalysis : AggregateRoot
{
    public const int MaxErrorLength = 1000;
    public const int MaxAssignments = 400;

    // Por debajo de esto la propuesta se enseña con un aviso: sin balón a la vista la posesión baila, y sin el campo
    // calibrado no salen ni los tercios ni los eventos de geometría (fueras, tiros, córners).
    public const decimal LowBallFramesPct = 35m;
    public const decimal LowCalibratedFramesPct = 50m;

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

    // Lo último que el cuerpo técnico volcó al acta: cuál de los dos equipos detectados es el suyo y a qué ficha
    // corresponde cada jugador seguido (JSON tracklet → ficha), para no repetir la asignación al volver.
    public int? OurTeam { get; private set; }
    public string? AssignmentsJson { get; private set; }
    public DateTimeOffset? AppliedAt { get; private set; }

    public bool IsActive => Status is VideoAnalysisStatus.Pending or VideoAnalysisStatus.Running;

    public bool GenericPitch => PitchLengthM == GenericPitchLengthM && PitchWidthM == GenericPitchWidthM;

    public bool LowQuality =>
        Status == VideoAnalysisStatus.Ready && (BallFramesPct < LowBallFramesPct || CalibratedFramesPct < LowCalibratedFramesPct);

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
        ClearResult();
    }

    public void Start(DateTimeOffset now)
    {
        if (Status != VideoAnalysisStatus.Pending)
            throw new DomainException(ErrorCode.VideoAnalysisNotPending);

        Status = VideoAnalysisStatus.Running;
        Attempts++;
        Progress = 0;
        Error = null;
        StartedAt = now;
        FinishedAt = null;
    }

    public void ReportProgress(int percent)
    {
        EnsureRunning();
        Progress = Math.Clamp(percent, 0, 99);
    }

    public void Complete(VideoAnalysisOutcome outcome, DateTimeOffset now)
    {
        EnsureRunning();
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
        OurTeam = null;
        AssignmentsJson = null;
        AppliedAt = null;

        Status = VideoAnalysisStatus.Ready;
        Progress = 100;
        Error = null;
        FinishedAt = now;
    }

    public void Fail(string error, DateTimeOffset now)
    {
        EnsureRunning();
        Status = VideoAnalysisStatus.Failed;
        Error = string.IsNullOrWhiteSpace(error) ? "Error desconocido" : error.Length > MaxErrorLength ? error[..MaxErrorLength] : error;
        FinishedAt = now;
        ClearResult();
    }

    // El proceso que lo analizaba se paró a medias (apagado, reinicio): vuelve a la cola sin contar el intento.
    public void Requeue()
    {
        if (Status != VideoAnalysisStatus.Running) return;
        Status = VideoAnalysisStatus.Pending;
        Attempts = Math.Max(0, Attempts - 1);
        Progress = 0;
        StartedAt = null;
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
        OurTeam = null;
        AssignmentsJson = null;
        AppliedAt = null;
    }

    private void EnsureRunning()
    {
        if (Status != VideoAnalysisStatus.Running)
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
    Task<IReadOnlyList<MatchVideoAnalysis>> GetRunningAsync(CancellationToken cancellationToken = default);

    Task ReplaceCropsAsync(Guid analysisId, IReadOnlyCollection<VideoAnalysisCrop> crops, CancellationToken cancellationToken = default);
    Task<byte[]?> GetCropAsync(Guid analysisId, int tracklet, CancellationToken cancellationToken = default);
    Task<IReadOnlySet<int>> GetCropTrackletsAsync(Guid analysisId, CancellationToken cancellationToken = default);
}
