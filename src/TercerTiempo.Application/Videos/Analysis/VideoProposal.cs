using System.Text.Json;
using System.Text.Json.Serialization;
using TercerTiempo.Domain.OfficialSheets;

namespace TercerTiempo.Application.Videos.Analysis;

// acta-propuesta.json del analizador, leído sin fiarse de él: lo que falte vale cero y lo que no cuadre se recorta.
// Trae los datos de equipo desde los dos lados y una línea por jugador. Desde la versión 2, con el acta oficial, cada
// línea es un jugador identificado por el dorsal que se le lee en el vídeo (como mucho los que jugaron) y cada equipo dice
// de qué lado del acta es; antes (o sin acta), cada línea es un jugador seguido («tracklet») y el cuerpo técnico dice
// cuál es su equipo y asigna cada línea a una ficha.
public sealed record VideoProposal(IReadOnlyList<VideoProposalTeam> Teams, IReadOnlyList<VideoProposalPlayer> Players, bool Identified = false)
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
                    Side(t.Lado),
                    Color(t.Color),
                    s.PossessionPct is { } possession ? Math.Clamp(possession, 0, 100) : null,
                    Confidence(s.PossessionConfidence),
                    s.PossessionBallPct is { } ballPct ? Math.Clamp(Math.Round(ballPct, 1), 0, 100) : null,
                    shots,
                    Math.Min(Count(s.ShotsOnTargetFor), shots),
                    Count(s.CornersFor),
                    Count(s.ThrowInsFor),
                    Count(s.GoalKicksFor),
                    Count(s.GoalsFor),
                    Count(s.FoulsCommitted),
                    Count(s.OffsidesFor))
                {
                    Moments = TeamMoments(t.Momentos),
                };
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
                    p.Dorsal is >= 1 and <= 99 ? p.Dorsal : p.DorsalLlm is >= 1 and <= 99 ? p.DorsalLlm : null,
                    new VideoPlayerActions(
                        shots,
                        Math.Min(Count(a.ShotsOnTarget), shots),
                        Count(a.Goals),
                        Count(a.Saves),
                        Count(a.Recoveries),
                        Count(a.BallLosses),
                        passes,
                        passes + Count(a.PassesFailed),
                        Count(a.Touches),
                        Count(a.Fouls),
                        Count(a.FoulsReceived),
                        Count(a.Offsides)))
                {
                    ShirtFromVideo = p.Dorsal is >= 1 and <= 99,
                    SheetName = string.IsNullOrWhiteSpace(p.NombreActa) ? null : p.NombreActa.Trim(),
                    ShirtConfidence = p.ConfianzaDorsal is { } c ? Math.Clamp(Math.Round(c, 2), 0, 1) : null,
                    Pieces = Math.Max(1, p.Tramos ?? 1),
                    Moments = PlayerMoments(p.Momentos),
                };
            })
            .ToList();

        var unidentified = (file.Calidad?.SinIdentificarS ?? [])
            .Where(kv => kv.Key is "0" or "1")
            .ToDictionary(kv => int.Parse(kv.Key, System.Globalization.CultureInfo.InvariantCulture), kv => (int)Math.Round(Math.Max(0, kv.Value)));
        return new VideoProposal(teams, players, file.Identificados == true && players.Any(p => p.ShirtFromVideo)) { UnidentifiedSeconds = unidentified };
    }

    // Segundos con jugadores a la vista sin identificar, por equipo (0, 1).
    public IReadOnlyDictionary<int, int> UnidentifiedSeconds { get; init; } = new Dictionary<int, int>();

    public VideoProposalTeam? Team(int index) => Teams.FirstOrDefault(t => t.Index == index);

    // Con el acta oficial, el analizador dice de qué lado es cada equipo por los dorsales y cruza los nombres con ese
    // lado: el equipo de la plantilla es el de su lado y no se elige. Sin acta (o sin lado) no se sabe.
    public int? TeamOn(MatchSide side) => Identified ? Teams.FirstOrDefault(t => t.Side == side)?.Index : null;

    private static int Count(int? value) => Math.Max(0, value ?? 0);

    // Ningún dato de un partido llega a tantas veces: una lista más larga no es de fiar y se recorta.
    private const int MaxMoments = 2000;

    private static VideoTeamMoments? TeamMoments(TeamMomentsDto? m) => m is null
        ? null
        : new VideoTeamMoments(Seconds(m.Shots), Seconds(m.ShotsOnTarget), Seconds(m.Goals), Seconds(m.Corners), Seconds(m.ThrowIns), Seconds(m.GoalKicks), Seconds(m.Fouls), Seconds(m.Offsides));

    private static VideoPlayerMoments? PlayerMoments(PlayerMomentsDto? m) => m is null
        ? null
        : new VideoPlayerMoments(
            Seconds(m.Shots), Seconds(m.ShotsOnTarget), Seconds(m.Goals), Seconds(m.Saves), Seconds(m.Recoveries), Seconds(m.BallLosses),
            Seconds(m.Passes), Seconds(m.PassesFailed), Seconds(m.Fouls), Seconds(m.FoulsReceived), Seconds(m.Offsides));

    // Segundos del archivo de vídeo, en orden y a la décima; lo que no es un segundo (vacío o negativo) se descarta.
    private static IReadOnlyList<decimal> Seconds(List<decimal?>? values) => values is null
        ? []
        : values.OfType<decimal>().Where(v => v >= 0).Select(v => Math.Round(v, 1)).Order().Take(MaxMoments).ToList();

    private static MatchSide? Side(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "local" => MatchSide.Home,
        "visitante" => MatchSide.Away,
        _ => null,
    };

    private static string? Color(string? value) =>
        value is { Length: 7 } && value[0] == '#' && value.Skip(1).All(Uri.IsHexDigit) ? value.ToLowerInvariant() : null;

    // «alta», «media» o «baja» del analizador; sin el dato (análisis anteriores al 10-11-2026) no se sabe.
    private static VideoPossessionConfidence? Confidence(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "alta" or "high" => VideoPossessionConfidence.High,
        "media" or "medium" => VideoPossessionConfidence.Medium,
        "baja" or "low" => VideoPossessionConfidence.Low,
        _ => null,
    };

    private sealed class FileDto
    {
        public List<TeamDto>? Equipos { get; set; }
        public List<PlayerDto>? Jugadores { get; set; }
        public bool? Identificados { get; set; }
        public QualityDto? Calidad { get; set; }
    }

    private sealed class QualityDto
    {
        public Dictionary<string, decimal>? SinIdentificarS { get; set; }
    }

    private sealed class TeamDto
    {
        public int Id { get; set; } = -1;
        public string? Lado { get; set; }
        public string? Color { get; set; }
        public TeamStatsDto? TeamStats { get; set; }
        public TeamMomentsDto? Momentos { get; set; }
    }

    private sealed class TeamMomentsDto
    {
        public List<decimal?>? Shots { get; set; }
        public List<decimal?>? ShotsOnTarget { get; set; }
        public List<decimal?>? Goals { get; set; }
        public List<decimal?>? Corners { get; set; }
        public List<decimal?>? ThrowIns { get; set; }
        public List<decimal?>? GoalKicks { get; set; }
        public List<decimal?>? Fouls { get; set; }
        public List<decimal?>? Offsides { get; set; }
    }

    private sealed class TeamStatsDto
    {
        public int? PossessionPct { get; set; }
        public string? PossessionConfidence { get; set; }
        public decimal? PossessionBallPct { get; set; }
        public int? CornersFor { get; set; }
        public int? ShotsFor { get; set; }
        public int? ShotsOnTargetFor { get; set; }
        public int? ThrowInsFor { get; set; }
        public int? GoalKicksFor { get; set; }
        public int? GoalsFor { get; set; }
        public int? FoulsCommitted { get; set; }
        public int? OffsidesFor { get; set; }
    }

    private sealed class PlayerDto
    {
        public int Tracklet { get; set; } = -1;
        public int? Equipo { get; set; }
        public bool Portero { get; set; }
        public string? Recorte { get; set; }
        [JsonPropertyName("dorsal_llm")]
        public int? DorsalLlm { get; set; }
        public int? Dorsal { get; set; }
        public string? NombreActa { get; set; }
        public decimal? ConfianzaDorsal { get; set; }
        public int? Tramos { get; set; }
        public int? MinutosALaVista { get; set; }
        public string? Desde { get; set; }
        public string? Hasta { get; set; }
        public int? DistanciaM { get; set; }
        public ActionsDto? Propuesta { get; set; }
        public PlayerMomentsDto? Momentos { get; set; }
    }

    private sealed class PlayerMomentsDto
    {
        public List<decimal?>? Shots { get; set; }
        public List<decimal?>? ShotsOnTarget { get; set; }
        public List<decimal?>? Goals { get; set; }
        public List<decimal?>? Saves { get; set; }
        public List<decimal?>? Recoveries { get; set; }
        public List<decimal?>? BallLosses { get; set; }
        public List<decimal?>? Passes { get; set; }
        public List<decimal?>? PassesFailed { get; set; }
        public List<decimal?>? Fouls { get; set; }
        public List<decimal?>? FoulsReceived { get; set; }
        public List<decimal?>? Offsides { get; set; }
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
        public int? Fouls { get; set; }
        public int? FoulsReceived { get; set; }
        public int? Offsides { get; set; }
    }
}

// Cuánto se puede fiar uno de la posesión: el analizador solo la mide sobre el tiempo con balón visto o interpolado y
// la da por baja cuando ese tiempo es poco. Con baja se enseña, pero no se vuelca al acta.
public enum VideoPossessionConfidence
{
    Low = 1,
    Medium = 2,
    High = 3,
}

// Los datos de un equipo, «a favor»: lo que es en contra para un equipo es lo que tiene a favor el otro (las faltas que
// recibe uno son las que comete el otro, y los fueras de juego del rival son los que le pitan a él).
public sealed record VideoProposalTeam(
    int Index,
    MatchSide? Side,
    string? Color,
    int? PossessionPct,
    VideoPossessionConfidence? PossessionConfidence,
    decimal? PossessionBallPct,
    int Shots,
    int ShotsOnTarget,
    int Corners,
    int ThrowIns,
    int GoalKicks,
    int Goals,
    int Fouls = 0,
    int Offsides = 0)
{
    // La posesión que se puede llevar al acta: la hay y no es de confianza baja.
    public int? UsablePossessionPct => PossessionConfidence == VideoPossessionConfidence.Low ? null : PossessionPct;

    // Desde Ojeador 2.1.2; null en análisis anteriores.
    public VideoTeamMoments? Moments { get; init; }
}

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
    VideoPlayerActions Actions)
{
    // El dorsal se leyó en el vídeo (y, con el acta oficial, es el de alguien que jugó en esos minutos).
    public bool ShirtFromVideo { get; init; }

    // Nombre del jugador con ese dorsal en el acta oficial.
    public string? SheetName { get; init; }

    // Parte de las lecturas de sus tramos que dicen ese dorsal (0-1).
    public decimal? ShirtConfidence { get; init; }

    // Tramos seguidos que se han juntado en este jugador.
    public int Pieces { get; init; } = 1;

    // Desde Ojeador 2.1.2; null en análisis anteriores.
    public VideoPlayerMoments? Moments { get; init; }
}

// El segundo del archivo de vídeo (a la décima, el mismo archivo que se reproduce) de cada cosa que cuenta la propuesta
// de un jugador, para verla en el vídeo. Son las mismas que sus acciones, salvo los goles que salen del acta oficial
// cerrada sin que el vídeo los viera; de los pases, los completados y los fallados por separado.
public sealed record VideoPlayerMoments(
    IReadOnlyList<decimal> Shots,
    IReadOnlyList<decimal> ShotsOnTarget,
    IReadOnlyList<decimal> Goals,
    IReadOnlyList<decimal> Saves,
    IReadOnlyList<decimal> Recoveries,
    IReadOnlyList<decimal> BallLosses,
    IReadOnlyList<decimal> PassesCompleted,
    IReadOnlyList<decimal> PassesFailed,
    IReadOnlyList<decimal> Fouls,
    IReadOnlyList<decimal> FoulsReceived,
    IReadOnlyList<decimal> Offsides);

// Lo mismo para los datos de un equipo, «a favor»: los momentos en contra son los del otro equipo.
public sealed record VideoTeamMoments(
    IReadOnlyList<decimal> Shots,
    IReadOnlyList<decimal> ShotsOnTarget,
    IReadOnlyList<decimal> Goals,
    IReadOnlyList<decimal> Corners,
    IReadOnlyList<decimal> ThrowIns,
    IReadOnlyList<decimal> GoalKicks,
    IReadOnlyList<decimal> Fouls,
    IReadOnlyList<decimal> Offsides);

public sealed record VideoPlayerActions(
    int Shots,
    int ShotsOnTarget,
    int Goals,
    int Saves,
    int Recoveries,
    int BallLosses,
    int PassesCompleted,
    int PassesAttempted,
    int Touches,
    int Fouls = 0,
    int FoulsReceived = 0,
    int Offsides = 0)
{
    public static readonly VideoPlayerActions None = new(0, 0, 0, 0, 0, 0, 0, 0, 0);

    // Lo que se puede llevar al acta (los toques, las faltas recibidas y los fueras de juego no tienen casilla).
    public bool HasSheetData => Shots > 0 || Goals > 0 || Saves > 0 || Recoveries > 0 || BallLosses > 0 || PassesAttempted > 0 || Fouls > 0;

    public VideoPlayerActions Plus(VideoPlayerActions other) => new(
        Shots + other.Shots, ShotsOnTarget + other.ShotsOnTarget, Goals + other.Goals, Saves + other.Saves,
        Recoveries + other.Recoveries, BallLosses + other.BallLosses, PassesCompleted + other.PassesCompleted,
        PassesAttempted + other.PassesAttempted, Touches + other.Touches, Fouls + other.Fouls,
        FoulsReceived + other.FoulsReceived, Offsides + other.Offsides);
}
