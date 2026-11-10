using System.Text.Json;
using System.Text.Json.Serialization;

namespace TercerTiempo.Application.Videos.Analysis;

// acta-propuesta.json del analizador, leído sin fiarse de él: lo que falte vale cero y lo que no cuadre se recorta.
// Trae los datos de equipo desde los dos lados (el analizador no sabe cuál de los dos equipos es el del club) y una
// línea por jugador seguido («tracklet»), que el cuerpo técnico asigna a una ficha.
public sealed record VideoProposal(IReadOnlyList<VideoProposalTeam> Teams, IReadOnlyList<VideoProposalPlayer> Players)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { NumberHandling = JsonNumberHandling.AllowReadingFromString };

    public static VideoProposal Parse(string json)
    {
        var file = JsonSerializer.Deserialize<FileDto>(json, Json) ?? new FileDto();

        var teams = (file.Equipos ?? [])
            .Where(t => t.Id is 0 or 1)
            .GroupBy(t => t.Id)
            .Select(g => g.First())
            .OrderBy(t => t.Id)
            .Select(t =>
            {
                var s = t.TeamStats ?? new TeamStatsDto();
                var shots = Count(s.ShotsFor);
                return new VideoProposalTeam(
                    t.Id,
                    Color(t.Color),
                    s.PossessionPct is { } possession ? Math.Clamp(possession, 0, 100) : null,
                    shots,
                    Math.Min(Count(s.ShotsOnTargetFor), shots),
                    Count(s.CornersFor),
                    Count(s.ThrowInsFor),
                    Count(s.GoalKicksFor),
                    Count(s.GoalsFor));
            })
            .ToList();

        var players = (file.Jugadores ?? [])
            .Where(p => p.Tracklet >= 0 && p.Equipo is 0 or 1)
            .GroupBy(p => p.Tracklet)
            .Select(g => g.First())
            .Select(p =>
            {
                var a = p.Propuesta ?? new ActionsDto();
                var shots = Count(a.Shots);
                var passes = Count(a.Passes);
                return new VideoProposalPlayer(
                    p.Tracklet,
                    p.Equipo!.Value,
                    p.Portero,
                    string.IsNullOrWhiteSpace(p.Recorte) ? null : p.Recorte.Replace('\\', '/'),
                    Count(p.MinutosALaVista),
                    p.Desde ?? "",
                    p.Hasta ?? "",
                    Count(p.DistanciaM),
                    p.DorsalLlm is >= 1 and <= 99 ? p.DorsalLlm : null,
                    new VideoPlayerActions(
                        shots,
                        Math.Min(Count(a.ShotsOnTarget), shots),
                        Count(a.Goals),
                        Count(a.Saves),
                        Count(a.Recoveries),
                        Count(a.BallLosses),
                        passes,
                        passes + Count(a.PassesFailed),
                        Count(a.Touches)));
            })
            .ToList();

        return new VideoProposal(teams, players);
    }

    public VideoProposalTeam? Team(int index) => Teams.FirstOrDefault(t => t.Index == index);

    private static int Count(int? value) => Math.Max(0, value ?? 0);

    private static string? Color(string? value) =>
        value is { Length: 7 } && value[0] == '#' && value.Skip(1).All(Uri.IsHexDigit) ? value.ToLowerInvariant() : null;

    private sealed class FileDto
    {
        public List<TeamDto>? Equipos { get; set; }
        public List<PlayerDto>? Jugadores { get; set; }
    }

    private sealed class TeamDto
    {
        public int Id { get; set; } = -1;
        public string? Color { get; set; }
        public TeamStatsDto? TeamStats { get; set; }
    }

    private sealed class TeamStatsDto
    {
        public int? PossessionPct { get; set; }
        public int? CornersFor { get; set; }
        public int? ShotsFor { get; set; }
        public int? ShotsOnTargetFor { get; set; }
        public int? ThrowInsFor { get; set; }
        public int? GoalKicksFor { get; set; }
        public int? GoalsFor { get; set; }
    }

    private sealed class PlayerDto
    {
        public int Tracklet { get; set; } = -1;
        public int? Equipo { get; set; }
        public bool Portero { get; set; }
        public string? Recorte { get; set; }
        [JsonPropertyName("dorsal_llm")]
        public int? DorsalLlm { get; set; }
        public int? MinutosALaVista { get; set; }
        public string? Desde { get; set; }
        public string? Hasta { get; set; }
        public int? DistanciaM { get; set; }
        public ActionsDto? Propuesta { get; set; }
    }

    private sealed class ActionsDto
    {
        public int? Shots { get; set; }
        public int? ShotsOnTarget { get; set; }
        public int? Goals { get; set; }
        public int? Saves { get; set; }
        public int? Recoveries { get; set; }
        public int? BallLosses { get; set; }
        public int? Passes { get; set; }
        public int? PassesFailed { get; set; }
        public int? Touches { get; set; }
    }
}

// Los datos de un equipo, «a favor»: lo que es en contra para un equipo es lo que tiene a favor el otro.
public sealed record VideoProposalTeam(
    int Index,
    string? Color,
    int? PossessionPct,
    int Shots,
    int ShotsOnTarget,
    int Corners,
    int ThrowIns,
    int GoalKicks,
    int Goals);

public sealed record VideoProposalPlayer(
    int Tracklet,
    int Team,
    bool Goalkeeper,
    string? CropFile,
    int MinutesVisible,
    string From,
    string To,
    int DistanceM,
    int? ShirtNumber,
    VideoPlayerActions Actions);

public sealed record VideoPlayerActions(
    int Shots,
    int ShotsOnTarget,
    int Goals,
    int Saves,
    int Recoveries,
    int BallLosses,
    int PassesCompleted,
    int PassesAttempted,
    int Touches)
{
    public static readonly VideoPlayerActions None = new(0, 0, 0, 0, 0, 0, 0, 0, 0);

    // Lo que se puede llevar al acta (los toques no tienen casilla).
    public bool HasSheetData => Shots > 0 || Goals > 0 || Saves > 0 || Recoveries > 0 || BallLosses > 0 || PassesAttempted > 0;

    public VideoPlayerActions Plus(VideoPlayerActions other) => new(
        Shots + other.Shots, ShotsOnTarget + other.ShotsOnTarget, Goals + other.Goals, Saves + other.Saves,
        Recoveries + other.Recoveries, BallLosses + other.BallLosses, PassesCompleted + other.PassesCompleted,
        PassesAttempted + other.PassesAttempted, Touches + other.Touches);
}
