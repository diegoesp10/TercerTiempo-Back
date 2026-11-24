using System.ComponentModel;
using System.Text.Json;
using TercerTiempo.Application.Abstractions.Videos;
using TercerTiempo.Domain.OfficialSheets;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Videos;

namespace TercerTiempo.Application.Videos.Analysis;

public enum VideoAnalysisState
{
    // El vídeo está listo pero nadie ha pedido analizarlo (subido antes de que existiera el análisis, o desactivado).
    NotRequested,

    // El vídeo todavía se está comprimiendo: el análisis empieza solo cuando acabe.
    WaitingForVideo,

    Pending,
    Running,
    Ready,
    Failed,

    // El cuerpo técnico lo paró mientras esperaba o se analizaba; se puede volver a pedir.
    Cancelled,

    // En pausa: si se estaba analizando, el analizador espera donde iba sin perder nada; si estaba en cola, no se
    // empieza. Se reanuda cuando se pida.
    Paused,
}

public sealed record MatchVideoAnalysisDto(
    [property: Description("Se pueden pedir análisis en este servidor (Analysis:Enabled en la API). Los atiende el analizador que esté en marcha: el del servicio de vídeo o uno aparte con GPU (Start-Analyzer.ps1); si no hay ninguno, la petición espera en cola.")]
    bool Enabled,
    [property: Description("Club propietario o cuerpo técnico vigente: puede relanzar el análisis y volcarlo al acta. El administrador solo lo ve.")]
    bool CanManage,
    [property: Description("Un elemento por vídeo de la plantilla en este partido (el partido entero, o la primera y la segunda parte); vacío si no hay vídeo.")]
    IReadOnlyList<VideoAnalysisPartDto> Videos,
    [property: Description("Algún analizador (Start-Analyzer.ps1 o el servicio de vídeo con Analysis:RunWorker) ha dado señales en los últimos 2 minutos. Si no, lo que esté en cola no avanza.")]
    bool AnalyzerOnline = false,
    [property: Description("Última señal de un analizador; null si nunca ha habido ninguno.")]
    DateTimeOffset? AnalyzerSeenAt = null)
{
    [Description("El acta oficial del partido está cerrada: al volcar, los minutos (por descarte), la titularidad, los goles y las tarjetas salen de ella.")]
    public bool OfficialSheetClosed { get; init; }
}

public sealed record VideoAnalysisPartDto(
    Guid VideoId,
    VideoPart Part,
    string Title,
    VideoStatus VideoStatus,
    [property: Description("Avance de la compresión del vídeo en %, mientras está en Processing.")]
    int VideoProgress,
    [property: Description("Duración del vídeo en segundos, cuando se conoce: el análisis tarda más o menos lo mismo.")]
    int? DurationSeconds,
    [property: Description("NotRequested, WaitingForVideo (se está comprimiendo), Pending (en cola), Running, Ready, Failed o Cancelled.")]
    VideoAnalysisState State,
    [property: Description("Avance del análisis en %, mientras está en Running.")]
    int Progress,
    [property: Description("Por qué no se pudo analizar (sin GPU, sin analizador, vídeo ilegible...). En Pending, el motivo del intento anterior si se está reintentando.")]
    string? Error,
    DateTimeOffset? RequestedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    [property: Description("Nombre del analizador que analizó el vídeo (p. ej. «Ojeador»).")]
    string? AnalyzerName,
    [property: Description("Versión del analizador con la que se analizó.")]
    string? AnalyzerVersion,
    [property: Description("Modelos con los que se analizó: arquitectura, entrenamiento, motor y huella de cada uno, separados por « · ». Solo para el administrador; null para los demás.")]
    string? Weights,
    VideoAnalysisQualityDto? Quality,
    [property: Description("Los dos equipos que ha separado el analizador por su equipación. No sabe cuál es cuál: lo dice el cuerpo técnico.")]
    IReadOnlyList<VideoAnalysisTeamDto> Teams,
    [property: Description("Cuál de los dos equipos (0 o 1) dijo el cuerpo técnico que era el suyo la última vez que volcó; null si aún no.")]
    int? OurTeam,
    DateTimeOffset? AppliedAt,
    [property: Description("Jugadores seguidos de los dos equipos, cada uno con su recorte y lo que propone el vídeo para su línea.")]
    IReadOnlyList<VideoAnalysisPlayerDto> Players,
    [property: Description("Lo que está haciendo ahora el analizador (etapa, minuto del vídeo, jugadores seguidos, balón visto, velocidad, lo que le queda…), mientras está en Running o Paused; null si aún no ha contado nada.")]
    VideoAnalysisLive? Live = null,
    [property: Description("Cuándo contó el analizador ese estado por última vez.")]
    DateTimeOffset? LiveAt = null,
    [property: Description("Lo que ha ido sabiendo el analizador en esta pasada (las últimas 40 líneas, de la más antigua a la más reciente).")]
    IReadOnlyList<VideoAnalysisLogEntry>? Log = null,
    [property: Description("Desde cuándo está en pausa (Paused).")]
    DateTimeOffset? PausedAt = null,
    [property: Description("Segundos que ha estado en pausa en total en esta pasada (no cuentan en lo que tarda).")]
    int PausedSeconds = 0,
    [property: Description("En pausa a medias: el analizador lo tiene y espera donde iba. false si se pausó en cola (o el analizador se reinició): al reanudarlo, empieza desde el principio.")]
    bool PausedMidway = false,
    [property: Description("Cada jugador de la propuesta es una persona identificada por el dorsal leído en el vídeo y cruzado con el acta oficial (como mucho los que jugaron). false en análisis sin acta o anteriores: cada línea es un tramo seguido.")]
    bool Identified = false,
    [property: Description("El equipo (0 o 1) que por los dorsales es el de la plantilla según el acta oficial; null si no se sabe.")]
    int? SuggestedOurTeam = null,
    [property: Description("Segundos del vídeo con jugadores de la plantilla a la vista sin identificar, por equipo.")]
    IReadOnlyDictionary<int, int>? UnidentifiedSeconds = null);

public sealed record VideoAnalysisQualityDto(
    int Frames,
    [property: Description("% de fotogramas analizados con el balón a la vista.")]
    decimal BallFramesPct,
    [property: Description("% de fotogramas con el campo calibrado (sin él no salen tercios, fueras, tiros ni córners).")]
    decimal CalibratedFramesPct,
    [property: Description("Segundos de vídeo analizados.")]
    int AnalyzedSeconds,
    bool LowBall,
    bool LowCalibration,
    [property: Description("Medidas del campo con las que se calibró: las que apuntó el club del equipo local o, si no, las genéricas del analizador (120 × 70).")]
    decimal? PitchLengthM,
    decimal? PitchWidthM,
    [property: Description("El club del equipo local no había apuntado las medidas de su campo: se calibró con las genéricas.")]
    bool GenericPitch,
    [property: Description("% de los fotogramas del tramo que el analizador pudo leer del archivo; null en análisis anteriores al dato.")]
    decimal? ReadFramesPct = null,
    [property: Description("No se leyó al menos el 95 % de los fotogramas (archivo dañado o cortado): la propuesta sale de menos.")]
    bool LowRead = false);

public sealed record VideoAnalysisTeamDto(
    int Index,
    [property: Description("Lado del acta oficial (Home, Away) que tiene este equipo según los dorsales; null si no se sabe.")]
    MatchSide? Side,
    [property: Description("Color medio de la equipación (#rrggbb).")]
    string? Color,
    int Players,
    int? PossessionPct,
    [property: Description("Confianza de la posesión (Low, Medium, High): el analizador la mide solo sobre el tiempo con balón. Con Low se enseña pero no se vuelca al acta; null en análisis anteriores.")]
    VideoPossessionConfidence? PossessionConfidence,
    [property: Description("% del tramo con balón visto o interpolado, sobre el que se midió la posesión.")]
    decimal? PossessionBallPct,
    int Shots,
    int ShotsOnTarget,
    int Corners,
    int ThrowIns,
    int GoalKicks,
    [property: Description("Goles que el analizador da por confirmados: con el acta oficial cerrada, como mucho los suyos.")]
    int Goals,
    [property: Description("Faltas que comete: el juego se para con el balón en el campo, el que lo tenía es del otro equipo y un jugador de este estaba a menos de 5 m. Las que recibe son las que comete el otro equipo.")]
    int Fouls = 0,
    [property: Description("Fueras de juego que le pitan. Los del rival son los que le pitan al otro equipo.")]
    int Offsides = 0);

public sealed record VideoAnalysisPlayerDto(
    [property: Description("Número con el que el analizador sigue a esta persona; se corta al perderla de vista, así que un jugador puede tener varios.")]
    int Tracklet,
    int Team,
    bool Goalkeeper,
    [property: Description("Recorte del jugador; hay que pedirlo con la sesión iniciada.")]
    string? CropUrl,
    int MinutesVisible,
    string From,
    string To,
    int DistanceM,
    [property: Description("Dorsal: el leído en el vídeo (Identified) o, en análisis anteriores, el del modelo de lenguaje si se usó.")]
    int? ShirtNumber,
    VideoPlayerActions Proposal,
    [property: Description("Ficha a la que se asignó la última vez que se volcó; null si no se ha asignado.")]
    Guid? SquadPlayerId,
    [property: Description("Nombre del jugador con ese dorsal en el acta oficial.")]
    string? SheetName = null,
    [property: Description("Parte de las lecturas que dicen ese dorsal (0-1).")]
    decimal? ShirtConfidence = null,
    [property: Description("Ficha de la plantilla que por el dorsal y el acta oficial es este jugador; null si no se sabe o es del rival.")]
    Guid? SuggestedSquadPlayerId = null,
    [property: Description("Minutos que jugó según el acta oficial cerrada (por descarte: sin cambio, el partido entero), aunque el vídeo no lo viera todo el rato; null sin acta cerrada o sin identificar.")]
    int? SheetMinutes = null);

// Lo que se sabe del acta oficial para proponer la ficha de cada jugador identificado: el lado de la plantilla y, por
// dorsal, la ficha cruzada con la línea del acta que lo lleva.
internal sealed record VideoSheetHints(MatchSide OurSide, IReadOnlyDictionary<int, Guid> SquadPlayerByShirt)
{
    public static readonly VideoSheetHints None = new(MatchSide.Home, new Dictionary<int, Guid>());

    // Con el acta cerrada, los minutos de cada dorsal de cada lado (sin los dorsales repetidos).
    public IReadOnlyDictionary<(MatchSide Side, int Shirt), int> SheetMinutes { get; init; } = new Dictionary<(MatchSide, int), int>();
}

public sealed record ApplyVideoAnalysisResultDto(
    Squads.MatchSheetDto Sheet,
    MatchVideoAnalysisDto Analysis,
    [property: Description("Datos de equipo que han cambiado en el acta.")]
    int TeamValuesWritten,
    [property: Description("Líneas de jugador creadas o cambiadas.")]
    int LinesWritten,
    [property: Description("Jugadores asignados para los que el vídeo no propone nada que cambiar.")]
    int PlayersWithoutChanges,
    [property: Description("Líneas creadas o cambiadas solo con el acta oficial cerrada: jugaron, pero el vídeo no los identificó o no se asignaron.")]
    int LinesFromOfficialSheet = 0);

public sealed record VideoAnalysisCropDto(byte[] Content, string ContentType);

internal static class VideoAnalysisMapper
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static string CropRoute(Guid squadId, Guid matchId, Guid videoId, int tracklet, DateTimeOffset? version) =>
        $"/api/squads/{squadId}/matches/{matchId}/video-analysis/crops/{tracklet}?videoId={videoId}&v={version?.ToUnixTimeSeconds() ?? 0}";

    public static VideoAnalysisPartDto ToDto(
        Squad squad, MatchVideo video, MatchVideoAnalysis? analysis, IReadOnlySet<int> crops, VideoSheetHints? hints = null, bool showModels = false)
    {
        hints ??= VideoSheetHints.None;
        var state = analysis?.Status switch
        {
            VideoAnalysisStatus.Pending => VideoAnalysisState.Pending,
            VideoAnalysisStatus.Running => VideoAnalysisState.Running,
            VideoAnalysisStatus.Ready => VideoAnalysisState.Ready,
            VideoAnalysisStatus.Failed => VideoAnalysisState.Failed,
            VideoAnalysisStatus.Cancelled => VideoAnalysisState.Cancelled,
            VideoAnalysisStatus.Paused => VideoAnalysisState.Paused,
            _ => MatchVideoAnalysis.CanAnalyze(video) ? VideoAnalysisState.NotRequested : VideoAnalysisState.WaitingForVideo,
        };

        var proposal = analysis is { Status: VideoAnalysisStatus.Ready, ProposalJson: { } json } ? VideoProposal.Parse(json) : null;
        var assignments = Assignments(analysis?.AssignmentsJson);
        var squadPlayers = squad.Players.Select(p => p.Id).ToHashSet();

        var identified = proposal is { Identified: true };
        var suggestedTeam = proposal?.TeamOn(hints.OurSide);

        var teams = proposal?.Teams
            .Select(t => new VideoAnalysisTeamDto(
                t.Index, t.Side, t.Color, proposal.Players.Count(p => p.Team == t.Index), t.PossessionPct, t.PossessionConfidence, t.PossessionBallPct, t.Shots, t.ShotsOnTarget,
                t.Corners, t.ThrowIns, t.GoalKicks, t.Goals, t.Fouls, t.Offsides))
            .ToList() ?? [];

        var players = proposal?.Players
            .OrderBy(p => p.Team)
            .ThenBy(p => identified ? p.ShirtNumber ?? 100 : 0)
            .ThenByDescending(p => p.MinutesVisible).ThenBy(p => p.Tracklet)
            .Select(p => new VideoAnalysisPlayerDto(
                p.Tracklet, p.Team, p.Goalkeeper,
                crops.Contains(p.Tracklet) ? CropRoute(squad.Id, video.MatchId, video.Id, p.Tracklet, analysis!.FinishedAt) : null,
                p.MinutesVisible, p.From, p.To, p.DistanceM, p.ShirtNumber, p.Actions,
                assignments.TryGetValue(p.Tracklet, out var assigned) && squadPlayers.Contains(assigned) ? assigned : null,
                p.SheetName,
                p.ShirtConfidence,
                identified && p.ShirtFromVideo && p.Team == suggestedTeam && p.ShirtNumber is { } shirt
                    && hints.SquadPlayerByShirt.TryGetValue(shirt, out var suggested) && squadPlayers.Contains(suggested)
                    ? suggested
                    : null,
                identified && p.ShirtFromVideo && p.ShirtNumber is { } number && proposal.Team(p.Team)?.Side is { } teamSide
                    && hints.SheetMinutes.TryGetValue((teamSide, number), out var sheetMinutes)
                    ? sheetMinutes
                    : null))
            .ToList() ?? [];

        var quality = analysis is { Status: VideoAnalysisStatus.Ready, Frames: { } frames, BallFramesPct: { } ball, CalibratedFramesPct: { } calibrated }
            ? new VideoAnalysisQualityDto(
                frames, ball, calibrated, analysis.AnalyzedSeconds ?? 0,
                ball < MatchVideoAnalysis.LowBallFramesPct, calibrated < MatchVideoAnalysis.LowCalibratedFramesPct,
                analysis.PitchLengthM, analysis.PitchWidthM, analysis.GenericPitch,
                analysis.ReadFramesPct, analysis.ReadFramesPct < MatchVideoAnalysis.LowReadFramesPct)
            : null;

        return new VideoAnalysisPartDto(
            video.Id, video.Part, video.Title, video.Status, video.ProcessingProgress, video.DurationSeconds, state,
            analysis?.Progress ?? 0, analysis?.Error, analysis?.RequestedAt, analysis?.StartedAt, analysis?.FinishedAt,
            analysis?.AnalyzerName, analysis?.AnalyzerVersion, showModels ? analysis?.Weights : null, quality, teams, analysis?.OurTeam, analysis?.AppliedAt, players,
            analysis is { IsActive: true } ? VideoAnalysisProcessor.ReadLive(analysis.LiveJson) : null,
            analysis is { IsActive: true } ? analysis.LiveAt : null,
            analysis is null ? [] : analysis.Log,
            analysis?.PausedAt,
            analysis?.PausedSeconds ?? 0,
            analysis is { Status: VideoAnalysisStatus.Paused, IsWorking: true },
            identified,
            suggestedTeam,
            identified ? proposal!.UnidentifiedSeconds : null);
    }

    public static IReadOnlyDictionary<int, Guid> Assignments(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new Dictionary<int, Guid>();
        try
        {
            return JsonSerializer.Deserialize<Dictionary<int, Guid>>(json, Json) ?? [];
        }
        catch (JsonException)
        {
            return new Dictionary<int, Guid>();
        }
    }

    public static string AssignmentsJson(IEnumerable<KeyValuePair<int, Guid>> assignments) =>
        JsonSerializer.Serialize(assignments.ToDictionary(a => a.Key, a => a.Value), Json);
}
