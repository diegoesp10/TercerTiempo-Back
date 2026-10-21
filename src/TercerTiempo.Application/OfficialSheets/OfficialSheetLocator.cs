using Microsoft.Extensions.Logging;
using TercerTiempo.Application.Abstractions.OfficialSheets;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Competitions;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.Teams;

namespace TercerTiempo.Application.OfficialSheets;

/// <summary>
/// Localiza en la federación el acta de un partido: primero la competición (y la guarda en la temporada), después
/// la jornada, cuyos partidos se emparejan con los nuestros por los nombres de los equipos. Todo lo que se
/// resuelve (equipos, actas de toda la jornada) queda guardado para no volver a buscarlo.
/// </summary>
internal sealed class OfficialSheetLocator(
    IOfficialSheetProvider provider,
    IMatchStore matches,
    ITeamStore teams,
    ILogger<OfficialSheetLocator> logger)
{
    /// <summary>Parecido mínimo (local + visitante, sobre 2) para dar por bueno un emparejamiento.</summary>
    private const double MinPairScore = 1.2;

    public async Task<ExternalRef?> LocateAsync(Match match, Competition competition, Season season, CancellationToken cancellationToken)
    {
        if (match.OfficialRef is { } already && already.Provider == provider.ProviderName)
            return already;

        var competitionRef = await ResolveCompetitionAsync(competition, season, cancellationToken);
        if (competitionRef is null)
            return null;

        var fixtures = await provider.GetMatchdayAsync(competitionRef, match.Matchday, cancellationToken);
        if (fixtures.Count == 0)
        {
            logger.LogInformation("{Provider}: la jornada {Matchday} de {Competition} no tiene partidos publicados", provider.ProviderName, match.Matchday, competition.Name);
            return null;
        }

        var matchday = (await matches.GetBySeasonAsync(season.Id, cancellationToken))
            .Where(m => m.Matchday == match.Matchday)
            .ToList();

        var teamIds = matchday.SelectMany(m => new[] { m.HomeTeamId, m.AwayTeamId }).Distinct();
        var teamMap = (await teams.GetByIdsAsync(teamIds, cancellationToken)).ToDictionary(t => t.Id);

        foreach (var (ours, fixture) in Pair(matchday, fixtures, teamMap))
        {
            if (fixture.SheetCode is { Length: > 0 } code)
                ours.LinkOfficial(ExternalRef.Create(provider.ProviderName, code));

            LinkTeam(teamMap.GetValueOrDefault(ours.HomeTeamId), fixture.HomeCode);
            LinkTeam(teamMap.GetValueOrDefault(ours.AwayTeamId), fixture.AwayCode);
        }

        if (match.OfficialRef is null)
            logger.LogInformation("{Provider}: no se ha encontrado el partido {MatchId} (jornada {Matchday} de {Competition})", provider.ProviderName, match.Id, match.Matchday, competition.Name);

        return match.OfficialRef;
    }

    private async Task<OfficialCompetitionRef?> ResolveCompetitionAsync(Competition competition, Season season, CancellationToken cancellationToken)
    {
        if (season.OfficialRef is { } saved && saved.Provider == provider.ProviderName && OfficialCompetitionRef.Parse(saved.ExternalId) is { } parsed)
            return parsed;

        var found = await provider.FindCompetitionAsync(competition.Category, competition.Group, season.Label, cancellationToken);
        if (found is null)
        {
            logger.LogInformation("{Provider}: no se ha encontrado la competición {Competition} ({Season})", provider.ProviderName, competition.Name, season.Label);
            return null;
        }

        season.LinkOfficial(ExternalRef.Create(provider.ProviderName, found.ToString()));
        return found;
    }

    private void LinkTeam(Team? team, string? code)
    {
        if (team is null || string.IsNullOrEmpty(code)) return;
        if (team.OfficialRef?.Provider == provider.ProviderName) return;

        team.LinkOfficial(ExternalRef.Create(provider.ProviderName, code));
    }

    /// <summary>Empareja nuestros partidos con los del proveedor por parecido de nombres, de mejor a peor y sin repetir.</summary>
    internal static IEnumerable<(Match Ours, OfficialFixture Theirs)> Pair(
        IReadOnlyList<Match> ours, IReadOnlyList<OfficialFixture> theirs, IReadOnlyDictionary<Guid, Team> teamMap)
    {
        var candidates = new List<(double Score, Match Ours, OfficialFixture Theirs)>();

        foreach (var match in ours)
        {
            if (!teamMap.TryGetValue(match.HomeTeamId, out var home) || !teamMap.TryGetValue(match.AwayTeamId, out var away))
                continue;

            foreach (var fixture in theirs)
            {
                var score = ScoreFixture(home, away, fixture);
                if (score >= MinPairScore)
                    candidates.Add((score, match, fixture));
            }
        }

        var usedOurs = new HashSet<Guid>();
        var usedTheirs = new HashSet<OfficialFixture>();

        foreach (var (_, match, fixture) in candidates.OrderByDescending(c => c.Score))
        {
            if (usedOurs.Contains(match.Id) || usedTheirs.Contains(fixture)) continue;
            usedOurs.Add(match.Id);
            usedTheirs.Add(fixture);
            yield return (match, fixture);
        }
    }

    private static double ScoreFixture(Team home, Team away, OfficialFixture fixture)
    {
        // Con el código del equipo ya guardado el emparejamiento es exacto.
        var homeScore = home.OfficialRef is { } h && h.ExternalId == fixture.HomeCode ? 1.0 : TeamNameSimilarity.Score(home.Name, fixture.HomeName);
        var awayScore = away.OfficialRef is { } a && a.ExternalId == fixture.AwayCode ? 1.0 : TeamNameSimilarity.Score(away.Name, fixture.AwayName);
        return homeScore + awayScore;
    }
}
