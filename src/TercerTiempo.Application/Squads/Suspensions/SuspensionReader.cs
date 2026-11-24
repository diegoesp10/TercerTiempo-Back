using System.ComponentModel;
using TercerTiempo.Application.OfficialSheets;
using TercerTiempo.Application.Trainings;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.OfficialSheets;
using TercerTiempo.Domain.Performances;
using TercerTiempo.Domain.Sanctions;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Teams;

namespace TercerTiempo.Application.Squads.Suspensions;

public sealed record SuspensionBriefDto(
    [property: Description("YellowCards (ciclo de cinco amarillas), SecondYellow (expulsado por doble amarilla), RedCard (roja directa) o Committee (lo que ha publicado el comité de competición).")]
    SuspensionCause Cause,
    [property: Description("Por qué, para leer: «5.ª amarilla en la jornada 7», «Sanción del comité por la jornada 8: agredir a otro (2.º de 4 partidos)»…")]
    string Reason,
    Guid FromMatchId,
    int FromMatchday,
    [property: Description("De cuántos partidos es la sanción (las de las tarjetas, de uno).")]
    int Matches = 1,
    [property: Description("Cuál de esos partidos se cumple en este (1, 2…).")]
    int Nth = 1,
    [property: Description("Último día de una inhabilitación por días (cuerpo técnico); null en las de partidos.")]
    DateOnly? Until = null);

// Las sanciones de una plantilla con las tarjetas de sus partidos de liga. En cada partido manda el acta oficial cerrada,
// que es la que usa el comité de competición; mientras no llega (puede tardar una semana), la interna. Un jugador que no
// sale en el acta oficial (el cruce por nombre no lo encuentra) se queda con lo de la interna. Lo que el comité publica
// de un jugador manda sobre sus tarjetas (ver SuspensionCalendar).
internal sealed class SuspensionReader(
    IMatchStore matches,
    IPerformanceStore performances,
    IOfficialSheetStore sheets,
    ICommitteeSanctionStore committee,
    ITeamStore teams,
    TimeProvider clock)
{
    private const double MinTeamScore = 0.8;

    public async Task<SuspensionCalendar> LoadAsync(Squad squad, CancellationToken cancellationToken) =>
        await LoadAsync(squad, await matches.GetTeamMatchesAsync(squad.SeasonId, squad.TeamId, cancellationToken), cancellationToken);

    public async Task<SuspensionCalendar> LoadAsync(Squad squad, IReadOnlyList<Match> season, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var past = season.Where(m => m.KickoffAt <= now && m.Status is not (MatchStatus.Cancelled or MatchStatus.Postponed)).ToList();
        if (past.Count == 0) return SuspensionCalendar.Empty;

        var internalLines = (await performances.GetBySquadAsync(squad.Id, cancellationToken)).ToLookup(p => p.MatchId);
        var official = (await sheets.GetAvailableLinesByMatchIdsAsync(past.Select(m => m.Id), cancellationToken))
            .Where(s => s.IsAvailable)
            .GroupBy(s => s.MatchId)
            .ToDictionary(g => g.Key, g => g.First());

        var cards = new Dictionary<Guid, IReadOnlyDictionary<Guid, MatchCards>>();
        var playerByLine = new Dictionary<Guid, Guid>();
        foreach (var match in past)
        {
            var byPlayer = internalLines[match.Id]
                .GroupBy(p => p.SquadPlayerId)
                .ToDictionary(g => g.Key, g => new MatchCards(g.First().YellowCards, g.First().RedCard));
            if (official.TryGetValue(match.Id, out var sheet))
            {
                foreach (var (playerId, line) in SheetMatcher.Match(squad.Players, sheet.LinesOf(SideOf(match, squad))))
                {
                    byPlayer[playerId] = new MatchCards(line.YellowCards, line.RedCard);
                    playerByLine[line.Id] = playerId;
                }
            }
            if (byPlayer.Count > 0) cards[match.Id] = byPlayer;
        }

        var decisions = await CommitteeDecisionsAsync(squad, past, official, playerByLine, cancellationToken);
        return SuspensionCalendar.Build(season, cards, decisions);
    }

    // Lo que ha decidido el comité sobre cada partido ya jugado. Un partido está visto cuando su jornada sale en una
    // reunión del mismo día o posterior (un aplazado de la jornada 5 jugado después de la reunión que trató esa jornada
    // aún no lo está). Los nombres del comité son los de la federación, como los de las actas: primero se busca la misma
    // persona en el acta oficial del partido (ya cruzada con la plantilla) y, si no está, en la plantilla por nombre.
    private async Task<IReadOnlyDictionary<Guid, CommitteeDecision>> CommitteeDecisionsAsync(
        Squad squad, IReadOnlyList<Match> past, IReadOnlyDictionary<Guid, OfficialMatchSheet> official, IReadOnlyDictionary<Guid, Guid> playerByLine,
        CancellationToken cancellationToken)
    {
        var resolutions = await committee.GetBySeasonAsync(squad.SeasonId, cancellationToken);
        if (resolutions.Count == 0) return new Dictionary<Guid, CommitteeDecision>();

        var ourNames = await TeamNamesAsync(squad, past, official, resolutions, cancellationToken);
        if (ourNames.Count == 0) return new Dictionary<Guid, CommitteeDecision>();
        var candidates = squad.Players.Select(p => new SheetMatcher.Candidate(p.Id, p.NormalizedName, null)).ToList();
        var result = new Dictionary<Guid, CommitteeDecision>();

        foreach (var match in past)
        {
            var playedOn = CallUps.DayOf(match.KickoffAt);
            var seen = resolutions.Where(r => r.Matchday == match.Matchday && r.MeetingOn >= playedOn).OrderBy(r => r.MeetingOn).ToList();
            if (seen.Count == 0) continue;

            var ours = seen
                .SelectMany(r => r.Sanctions
                    .Where(s => s.Kind == CommitteeSanctionKind.Player && ourNames.Contains(NameNormalizer.Normalize(s.TeamName)))
                    .Select(s => (Resolution: r, Sanction: s)))
                .ToList();
            if (ours.Count == 0)
            {
                result[match.Id] = CommitteeDecision.Nothing;
                continue;
            }

            var lines = new Dictionary<string, OfficialSheetLine>();
            if (official.TryGetValue(match.Id, out var sheet))
                foreach (var line in sheet.LinesOf(SideOf(match, squad)))
                    lines.TryAdd(line.NormalizedName, line);

            var fromSheet = new Dictionary<string, Guid>();
            foreach (var (_, sanction) in ours)
                if (lines.TryGetValue(sanction.NormalizedName, out var line) && playerByLine.TryGetValue(line.Id, out var playerId))
                    fromSheet[sanction.NormalizedName] = playerId;

            var unresolved = ours.Select(o => o.Sanction).Where(s => !fromSheet.ContainsKey(s.NormalizedName)).DistinctBy(s => s.NormalizedName).ToList();
            var fromName = unresolved.Count == 0
                ? new Dictionary<string, Guid>()
                : SheetMatcher.MatchNames(
                        candidates.Where(c => !fromSheet.ContainsValue(c.Id)),
                        unresolved.Select(s => new SheetMatcher.SheetName(s.Id, s.PersonName, s.NormalizedName, null)))
                    .ToDictionary(kv => kv.Value.NormalizedName, kv => kv.Key);

            var penalties = new Dictionary<Guid, List<CommitteePenalty>>();
            foreach (var (resolution, sanction) in ours)
            {
                if (!fromSheet.TryGetValue(sanction.NormalizedName, out var id) && !fromName.TryGetValue(sanction.NormalizedName, out id)) continue;
                if (!penalties.TryGetValue(id, out var list)) penalties[id] = list = [];
                list.Add(new CommitteePenalty(sanction.Matches, sanction.Reason, resolution.MeetingOn));
            }
            result[match.Id] = new CommitteeDecision(penalties.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<CommitteePenalty>)kv.Value));
        }

        return result;
    }

    // Cómo llama la federación al equipo: el nombre de sus actas oficiales y, sin ninguna todavía, el nombre más parecido
    // de los que salen en las sanciones (si es claro).
    private async Task<IReadOnlySet<string>> TeamNamesAsync(
        Squad squad, IReadOnlyList<Match> past, IReadOnlyDictionary<Guid, OfficialMatchSheet> official, IReadOnlyList<CommitteeResolution> resolutions,
        CancellationToken cancellationToken)
    {
        var names = past
            .Where(m => official.ContainsKey(m.Id))
            .Select(m => NameNormalizer.Normalize(m.IsHome(squad.TeamId) ? official[m.Id].HomeTeamName : official[m.Id].AwayTeamName))
            .Where(n => n.Length > 0)
            .ToHashSet();
        if (names.Count > 0) return names;

        if (await teams.GetByIdAsync(squad.TeamId, cancellationToken) is not { } team) return names;
        var scored = resolutions
            .SelectMany(r => r.Sanctions.Select(s => s.TeamName))
            .Distinct()
            .Select(n => (Name: n, Score: TeamNameSimilarity.Score(team.Name, n)))
            .Where(n => n.Score >= MinTeamScore)
            .OrderByDescending(n => n.Score)
            .ToList();
        if (scored.Count > 0 && (scored.Count == 1 || scored[1].Score < scored[0].Score))
            names.Add(NameNormalizer.Normalize(scored[0].Name));
        return names;
    }

    private static MatchSide SideOf(Match match, Squad squad) => match.IsHome(squad.TeamId) ? MatchSide.Home : MatchSide.Away;

    public static SuspensionBriefDto Brief(Suspension suspension) =>
        new(suspension.Cause, Reason(suspension), suspension.FromMatchId, suspension.FromMatchday, suspension.Matches, suspension.Nth);

    public static string Reason(Suspension suspension) => suspension.Cause switch
    {
        SuspensionCause.YellowCards => $"{suspension.Yellows}.ª amarilla en la jornada {suspension.FromMatchday}",
        SuspensionCause.SecondYellow => $"Expulsado por doble amarilla en la jornada {suspension.FromMatchday}",
        SuspensionCause.Committee => CommitteeReason(suspension),
        _ => $"Roja directa en la jornada {suspension.FromMatchday}",
    };

    private static string CommitteeReason(Suspension suspension)
    {
        var text = $"Sanción del comité por la jornada {suspension.FromMatchday}";
        if (suspension.Detail is { Length: > 0 } detail) text += $": {char.ToLowerInvariant(detail[0])}{detail[1..]}";
        return suspension.Matches > 1 ? $"{text} ({suspension.Nth}.º de {suspension.Matches} partidos)" : text;
    }

    // Para meterlo en mitad de una frase: «… sancionado para este partido (roja directa en la jornada 8)».
    public static string ReasonInSentence(Suspension suspension)
    {
        var text = Reason(suspension);
        return char.ToLowerInvariant(text[0]) + text[1..];
    }
}
