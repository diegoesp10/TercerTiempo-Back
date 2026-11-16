using Microsoft.EntityFrameworkCore;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Competitions;

namespace TercerTiempo.Infrastructure.Persistence.Stores;

internal sealed class CompetitionStore(AppDbContext db) : ICompetitionStore
{
    public Task<Competition?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.Competitions.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public Task<Competition?> GetByExternalRefAsync(ExternalRef externalRef, CancellationToken cancellationToken = default) =>
        db.Competitions.FirstOrDefaultAsync(
            c => c.ExternalRef!.Provider == externalRef.Provider && c.ExternalRef.ExternalId == externalRef.ExternalId,
            cancellationToken);

    public async Task<IReadOnlyList<Competition>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await db.Competitions.OrderBy(c => c.SortOrder).ThenBy(c => c.Name).ToListAsync(cancellationToken);

    public Task<Competition?> GetBySeasonAsync(Guid seasonId, CancellationToken cancellationToken = default) =>
        db.Competitions.FirstOrDefaultAsync(c => c.Seasons.Any(s => s.Id == seasonId), cancellationToken);

    public async Task<Competition?> GetByTeamAsync(Guid teamId, Guid? seasonYearId = null, CancellationToken cancellationToken = default)
    {
        var currentSeasons = (seasonYearId is { } year ? db.Seasons.Where(s => s.SeasonYearId == year) : db.Seasons.Where(s => s.IsCurrent))
            .Select(s => s.Id);

        var seasonId = await db.Standings
            .Where(s => s.TeamId == teamId && currentSeasons.Contains(s.SeasonId))
            .Select(s => s.SeasonId)
            .FirstOrDefaultAsync(cancellationToken);

        if (seasonId == Guid.Empty)
        {
            seasonId = await db.Matches
                .Where(m => (m.HomeTeamId == teamId || m.AwayTeamId == teamId) && currentSeasons.Contains(m.SeasonId))
                .Select(m => m.SeasonId)
                .FirstOrDefaultAsync(cancellationToken);
        }

        if (seasonId == Guid.Empty)
            return null;

        return await GetBySeasonAsync(seasonId, cancellationToken);
    }

    public async Task<IReadOnlyList<Competition>> GetBySeasonIdsAsync(IEnumerable<Guid> seasonIds, CancellationToken cancellationToken = default)
    {
        var list = seasonIds.Distinct().ToList();
        if (list.Count == 0) return [];
        return await db.Competitions.Where(c => c.Seasons.Any(s => list.Contains(s.Id))).ToListAsync(cancellationToken);
    }

    // Como GetByTeamAsync: la temporada de cada equipo sale de la clasificación y, si aún no está en ninguna, de sus
    // partidos; dos consultas para todos los equipos más una para sus ligas.
    public async Task<IReadOnlyDictionary<Guid, Competition>> GetByTeamsAsync(IEnumerable<Guid> teamIds, Guid? seasonYearId = null, CancellationToken cancellationToken = default)
    {
        var teams = teamIds.Distinct().ToList();
        if (teams.Count == 0) return new Dictionary<Guid, Competition>();

        var currentSeasons = (seasonYearId is { } year ? db.Seasons.Where(s => s.SeasonYearId == year) : db.Seasons.Where(s => s.IsCurrent))
            .Select(s => s.Id);

        var seasonByTeam = new Dictionary<Guid, Guid>();
        foreach (var row in await db.Standings
                     .Where(s => teams.Contains(s.TeamId) && currentSeasons.Contains(s.SeasonId))
                     .Select(s => new { s.TeamId, s.SeasonId })
                     .ToListAsync(cancellationToken))
            seasonByTeam.TryAdd(row.TeamId, row.SeasonId);

        var missing = teams.Where(t => !seasonByTeam.ContainsKey(t)).ToList();
        if (missing.Count > 0)
        {
            foreach (var row in await db.Matches
                         .Where(m => (missing.Contains(m.HomeTeamId) || missing.Contains(m.AwayTeamId)) && currentSeasons.Contains(m.SeasonId))
                         .Select(m => new { m.HomeTeamId, m.AwayTeamId, m.SeasonId })
                         .ToListAsync(cancellationToken))
            {
                if (missing.Contains(row.HomeTeamId)) seasonByTeam.TryAdd(row.HomeTeamId, row.SeasonId);
                if (missing.Contains(row.AwayTeamId)) seasonByTeam.TryAdd(row.AwayTeamId, row.SeasonId);
            }
        }

        if (seasonByTeam.Count == 0) return new Dictionary<Guid, Competition>();
        var bySeason = (await GetBySeasonIdsAsync(seasonByTeam.Values, cancellationToken))
            .SelectMany(c => c.Seasons.Select(s => (SeasonId: s.Id, Competition: c)))
            .ToDictionary(x => x.SeasonId, x => x.Competition);
        return seasonByTeam
            .Where(pair => bySeason.ContainsKey(pair.Value))
            .ToDictionary(pair => pair.Key, pair => bySeason[pair.Value]);
    }

    public void Add(Competition competition) => db.Competitions.Add(competition);
}
