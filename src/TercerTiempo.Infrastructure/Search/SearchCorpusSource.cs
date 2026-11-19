using Microsoft.EntityFrameworkCore;
using TercerTiempo.Application.Coaches.Photos;
using TercerTiempo.Application.Players.Photos;
using TercerTiempo.Application.Search;
using TercerTiempo.Application.Teams;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.Referees;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Teams;
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
        var squads = await db.Squads.AsNoTracking().IgnoreAutoIncludes().Include(s => s.Players).ToListAsync(cancellationToken);
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

        // Un resultado por persona: la web publica a algunos jugadores en dos fichas.
        var corpusPlayers = roster
            .GroupBy(r => (r.TeamId, r.SeasonId))
            .SelectMany(fichas => RosterPeople.Group(fichas))
            .Select(p => new CorpusPlayer(p.Main.Id, p.Main.TeamId, p.Main.FullName, p.Position, p.WithPhoto?.PhotoLink()))
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

        // Dónde pita cada árbitro: la categoría y la función en que más partidos tiene, para reconocerlo en los resultados.
        var categoryBySeason = competitions.SelectMany(c => c.Seasons.Select(s => (s.Id, c.Category))).ToDictionary(x => x.Id, x => x.Category);
        // Se cuenta en SQL: designaciones por árbitro, temporada y función, y partidos distintos por árbitro.
        var appearances = await (
                from o in db.Set<MatchOfficial>()
                join m in db.Matches on o.MatchId equals m.Id
                group o by new { o.RefereeId, m.SeasonId, o.Role } into g
                select new { g.Key.RefereeId, g.Key.SeasonId, g.Key.Role, Count = g.Count() })
            .ToListAsync(cancellationToken);
        var matchesByReferee = await (
                from o in db.Set<MatchOfficial>()
                join m in db.Matches on o.MatchId equals m.Id
                group o.MatchId by o.RefereeId into g
                select new { RefereeId = g.Key, Count = g.Distinct().Count() })
            .ToDictionaryAsync(x => x.RefereeId, x => x.Count, cancellationToken);
        var refereeStats = appearances.GroupBy(a => a.RefereeId).ToDictionary(g => g.Key, g =>
        {
            var usual = g.GroupBy(a => (Category: categoryBySeason.GetValueOrDefault(a.SeasonId), Duty: a.Role.Duty()))
                .OrderByDescending(x => x.Sum(a => a.Count)).ThenBy(x => x.Key.Duty).First().Key;
            return (Matches: matchesByReferee.GetValueOrDefault(g.Key), usual.Category, Assistant: usual.Duty == OfficialDuty.Assistant);
        });
        var corpusReferees = (await db.Set<Referee>().AsNoTracking().ToListAsync(cancellationToken))
            .Select(r => refereeStats.TryGetValue(r.Id, out var stats)
                ? new CorpusReferee(r.Id, r.Name, stats.Matches, stats.Category, stats.Assistant)
                : new CorpusReferee(r.Id, r.Name, 0))
            .ToList();

        // Cuerpo técnico con perfil: quien ha canjeado algún nombramiento, con sus equipos (los que el club le retiró antes de
        // empezar no cuentan).
        var today = TercerTiempo.Domain.Common.SpainTime.Today(DateTimeOffset.UtcNow);
        var appointments = await db.CoachAppointments.AsNoTracking()
            .Where(a => a.CoachId != null && (a.RevokedAt == null || a.RevokedAt > a.RedeemedAt))
            .Select(a => new { CoachId = a.CoachId!.Value, a.TeamId, a.Role, a.ValidFrom, a.ValidUntil, a.RevokedAt })
            .ToListAsync(cancellationToken);
        var coachIds = appointments.Select(a => a.CoachId).Distinct().ToList();
        var corpusCoaches = (await db.Coaches.AsNoTracking().Where(c => coachIds.Contains(c.Id)).ToListAsync(cancellationToken))
            .Select(c => new CorpusCoach(c.Id, c.DisplayName, c.PhotoLink(), appointments
                .Where(a => a.CoachId == c.Id)
                .Select(a => new CorpusCoachTeam(a.TeamId, a.Role, a.RevokedAt == null && today >= a.ValidFrom && today <= a.ValidUntil, a.ValidUntil))
                .ToList()))
            .ToList();

        return new SearchCorpus(corpusCompetitions, corpusTeams, corpusClubs, corpusPlayers, corpusMatches, corpusSquads, corpusReferees, corpusCoaches);
    }
}
