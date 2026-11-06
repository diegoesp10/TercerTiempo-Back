using System.ComponentModel;
using System.Text.Json;
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
}

public sealed record MatchVideoAnalysisDto(
    [property: Description("El análisis de vídeo está activado en este servidor (Analysis:Enabled); si no, no se puede pedir.")]
    bool Enabled,
    [property: Description("Club propietario o cuerpo técnico vigente: puede relanzar el análisis y volcarlo al acta. El administrador solo lo ve.")]
    bool CanManage,
    [property: Description("Un elemento por vídeo de la plantilla en este partido (el partido entero, o la primera y la segunda parte); vacío si no hay vídeo.")]
    IReadOnlyList<VideoAnalysisPartDto> Videos);

public sealed record VideoAnalysisPartDto(
    Guid VideoId,
    VideoPart Part,
    string Title,
    VideoStatus VideoStatus,
    [property: Description("Avance de la compresión del vídeo en %, mientras está en Processing.")]
    int VideoProgress,
    [property: Description("NotRequested, WaitingForVideo (se está comprimiendo), Pending (en cola), Running, Ready o Failed.")]
    VideoAnalysisState State,
    [property: Description("Avance del análisis en %, mientras está en Running.")]
    int Progress,
    [property: Description("Por qué no se pudo analizar (sin GPU, sin analizador, vídeo ilegible...).")]
    string? Error,
    DateTimeOffset? RequestedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    string? AnalyzerVersion,
    [property: Description("Pesos de los detectores con los que se analizó (públicos o afinados).")]
    string? Weights,
    VideoAnalysisQualityDto? Quality,
    [property: Description("Los dos equipos que ha separado el analizador por su equipación. No sabe cuál es cuál: lo dice el cuerpo técnico.")]
    IReadOnlyList<VideoAnalysisTeamDto> Teams,
    [property: Description("Cuál de los dos equipos (0 o 1) dijo el cuerpo técnico que era el suyo la última vez que volcó; null si aún no.")]
    int? OurTeam,
    DateTimeOffset? AppliedAt,
    [property: Description("Jugadores seguidos de los dos equipos, cada uno con su recorte y lo que propone el vídeo para su línea.")]
    IReadOnlyList<VideoAnalysisPlayerDto> Players);

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
    bool GenericPitch);

public sealed record VideoAnalysisTeamDto(
    int Index,
    [property: Description("Color medio de la equipación (#rrggbb).")]
    string? Color,
    int Players,
    int? PossessionPct,
    int Shots,
    int ShotsOnTarget,
    int Corners,
    int ThrowIns,
    int GoalKicks,
    [property: Description("Goles que el analizador da por confirmados: orientativo, el resultado es el del acta oficial.")]
    int Goals);

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
    [property: Description("Dorsal leído por el modelo de lenguaje, si se usó.")]
    int? ShirtNumber,
    VideoPlayerActions Proposal,
    [property: Description("Ficha a la que se asignó la última vez que se volcó; null si no se ha asignado.")]
    Guid? SquadPlayerId);

public sealed record ApplyVideoAnalysisResultDto(
    Squads.MatchSheetDto Sheet,
    MatchVideoAnalysisDto Analysis,
    [property: Description("Datos de equipo que han cambiado en el acta.")]
    int TeamValuesWritten,
    [property: Description("Líneas de jugador creadas o cambiadas.")]
    int LinesWritten,
    [property: Description("Jugadores asignados para los que el vídeo no propone nada que cambiar.")]
    int PlayersWithoutChanges);

public sealed record VideoAnalysisCropDto(byte[] Content, string ContentType);

internal static class VideoAnalysisMapper
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static string CropRoute(Guid squadId, Guid matchId, Guid videoId, int tracklet, DateTimeOffset? version) =>
        $"/api/squads/{squadId}/matches/{matchId}/video-analysis/crops/{tracklet}?videoId={videoId}&v={version?.ToUnixTimeSeconds() ?? 0}";

    public static VideoAnalysisPartDto ToDto(Squad squad, MatchVideo video, MatchVideoAnalysis? analysis, IReadOnlySet<int> crops)
    {
        var state = analysis?.Status switch
        {
            VideoAnalysisStatus.Pending => VideoAnalysisState.Pending,
            VideoAnalysisStatus.Running => VideoAnalysisState.Running,
            VideoAnalysisStatus.Ready => VideoAnalysisState.Ready,
            VideoAnalysisStatus.Failed => VideoAnalysisState.Failed,
            _ => MatchVideoAnalysis.CanAnalyze(video) ? VideoAnalysisState.NotRequested : VideoAnalysisState.WaitingForVideo,
        };

        var proposal = analysis is { Status: VideoAnalysisStatus.Ready, ProposalJson: { } json } ? VideoProposal.Parse(json) : null;
        var assignments = Assignments(analysis?.AssignmentsJson);
        var squadPlayers = squad.Players.Select(p => p.Id).ToHashSet();

        var teams = proposal?.Teams
            .Select(t => new VideoAnalysisTeamDto(
                t.Index, t.Color, proposal.Players.Count(p => p.Team == t.Index), t.PossessionPct, t.Shots, t.ShotsOnTarget,
                t.Corners, t.ThrowIns, t.GoalKicks, t.Goals))
            .ToList() ?? [];

        var players = proposal?.Players
            .OrderBy(p => p.Team).ThenByDescending(p => p.MinutesVisible).ThenBy(p => p.Tracklet)
            .Select(p => new VideoAnalysisPlayerDto(
                p.Tracklet, p.Team, p.Goalkeeper,
                crops.Contains(p.Tracklet) ? CropRoute(squad.Id, video.MatchId, video.Id, p.Tracklet, analysis!.FinishedAt) : null,
                p.MinutesVisible, p.From, p.To, p.DistanceM, p.ShirtNumber, p.Actions,
                assignments.TryGetValue(p.Tracklet, out var assigned) && squadPlayers.Contains(assigned) ? assigned : null))
            .ToList() ?? [];

        var quality = analysis is { Status: VideoAnalysisStatus.Ready, Frames: { } frames, BallFramesPct: { } ball, CalibratedFramesPct: { } calibrated }
            ? new VideoAnalysisQualityDto(
                frames, ball, calibrated, analysis.AnalyzedSeconds ?? 0,
                ball < MatchVideoAnalysis.LowBallFramesPct, calibrated < MatchVideoAnalysis.LowCalibratedFramesPct,
                analysis.PitchLengthM, analysis.PitchWidthM, analysis.GenericPitch)
            : null;

        return new VideoAnalysisPartDto(
            video.Id, video.Part, video.Title, video.Status, video.ProcessingProgress, state,
            analysis?.Progress ?? 0, analysis?.Error, analysis?.RequestedAt, analysis?.StartedAt, analysis?.FinishedAt,
            analysis?.AnalyzerVersion, analysis?.Weights, quality, teams, analysis?.OurTeam, analysis?.AppliedAt, players);
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
