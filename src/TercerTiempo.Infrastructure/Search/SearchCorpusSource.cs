using Microsoft.EntityFrameworkCore;
using TercerTiempo.Application.Players.Photos;
using TercerTiempo.Application.Search;
using TercerTiempo.Application.Teams;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Infrastructure.Persistence;

namespace TercerTiempo.Infrastructure.Search;

// Lee de una vez lo que entra en el buscador: solo las temporadas en curso de cada liga (lo que se consulta a diario) y
// sin seguir cambios (es de solo lectura).
internal sealed class SearchCorpusSource(AppDbContext db) : ISearchCorpusSource
{
    public async Task<SearchCorpus> LoadAsync(CancellationToken cancellationToken)
    {
        var competitions = await db.Competitions.AsNoTracking().ToListAsync(cancellationToken);
        var current = competitions
            .Select(c => (Competition: c, Season: c.CurrentSeason))
            .Where(x => x.Season is not null)
            .ToDictionary(x => x.Season!.Id, x => x.Competition);
        var seasonIds = current.Keys.ToList();

        var matches = await db.Matches.AsNoTracking().Where(m => seasonIds.Contains(m.SeasonId)).ToListAsync(cancellationToken);
        var standings = await db.Standings.AsNoTracking().Where(s => seasonIds.Contains(s.SeasonId)).ToListAsync(cancellationToken);
        var teams = await db.Teams.AsNoTracking().ToListAsync(cancellationToken);
        var roster = await db.RosterEntries.AsNoTracking().Where(r => seasonIds.Contains(r.SeasonId) && !r.Hidden).ToListAsync(cancellationToken);
        var clubs = await db.Clubs.AsNoTracking().ToListAsync(cancellationToken);
        var squads = await db.Squads.AsNoTracking().ToListAsync(cancellationToken);
        var linkedPlayers = squads.SelectMany(s => s.Players).Select(p => p.PlayerId).OfType<Guid>().Distinct().ToList();
        var accountPhotos = await db.Players.AsNoTracking()
            .Where(p => linkedPlayers.Contains(p.Id) && p.PhotoKey != null)
            .ToDictionaryAsync(p => p.Id, p => p.PhotoLink(), cancellationToken);
        var seasonLabels = await db.Seasons.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Label, cancellationToken);

        // Liga de cada equipo esta temporada: por la clasificación y, si aún no la hay, por sus partidos.
        var teamCompetition = new Dictionary<Guid, Guid>();
        foreach (var s in standings) teamCompetition.TryAdd(s.TeamId, current[s.SeasonId].Id);
        foreach (var m in matches)
        {
            teamCompetition.TryAdd(m.HomeTeamId, current[m.SeasonId].Id);
            teamCompetition.TryAdd(m.AwayTeamId, current[m.SeasonId].Id);
        }
        var standingByTeam = standings.GroupBy(s => s.TeamId).ToDictionary(g => g.Key, g => g.First());

        var corpusCompetitions = current.Select(kv =>
        {
            var seasonMatches = matches.Where(m => m.SeasonId == kv.Key).ToList();
            var teamCount = seasonMatches.SelectMany(m => new[] { m.HomeTeamId, m.AwayTeamId }).Distinct().Count();
            return new CorpusCompetition(
                kv.Value.Id, kv.Value.Name, kv.Value.Category, kv.Value.Group, kv.Value.Region, kv.Value.Tier, kv.Value.SortOrder,
                kv.Key, kv.Value.CurrentSeason!.Label, teamCount,
                seasonMatches.Count == 0 ? 0 : seasonMatches.Max(m => m.Matchday),
                seasonMatches.Count(m => m.Status == MatchStatus.Played));
        }).ToList();

        var corpusTeams = teams.Select(t =>
        {
            var standing = standingByTeam.GetValueOrDefault(t.Id);
            return new CorpusTeam(t.Id, t.Name, t.ShortName, t.CrestLink(), teamCompetition.TryGetValue(t.Id, out var c) ? c : null, standing?.Position, standing?.Points);
        }).ToList();

        var corpusPlayers = roster
            .Select(r => new CorpusPlayer(r.Id, r.TeamId, r.FullName, r.Position, r.ShirtNumber, r.PhotoLink()))
            .ToList();

        var corpusMatches = matches
            .Select(m => new CorpusMatch(m.Id, current[m.SeasonId].Id, m.Matchday, m.HomeTeamId, m.AwayTeamId, m.KickoffAt, m.Status, m.Score?.Home, m.Score?.Away))
            .ToList();

        var corpusClubs = clubs.Select(c => new CorpusClub(c.Id, c.Name, c.Teams.OrderBy(t => t.AddedAt).Select(t => t.TeamId).ToList())).ToList();

        var corpusSquads = squads.Select(s => new CorpusSquad(
            s.Id, s.TeamId, seasonLabels.GetValueOrDefault(s.SeasonId, ""),
            s.Players.Where(p => p.Status == SquadPlayerStatus.Active)
                .Select(p => new CorpusSquadPlayer(p.Id, p.FullName, p.Position, p.ShirtNumber, p.RosterEntryId,
                    p.PlayerId is { } playerId ? accountPhotos.GetValueOrDefault(playerId) : null))
                .ToList())).ToList();

        return new SearchCorpus(corpusCompetitions, corpusTeams, corpusClubs, corpusPlayers, corpusMatches, corpusSquads);
    }
}
