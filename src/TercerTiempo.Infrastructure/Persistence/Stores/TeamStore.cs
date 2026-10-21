using Microsoft.EntityFrameworkCore;
using TercerTiempo.Domain.Teams;

namespace TercerTiempo.Infrastructure.Persistence.Stores;

internal sealed class TeamStore(AppDbContext db) : ITeamStore
{
    public Task<Team?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.Teams.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Team>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default)
    {
        var list = ids.Distinct().ToList();
        if (list.Count == 0) return [];

        return await db.Teams.Where(t => list.Contains(t.Id)).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Team>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await db.Teams.OrderBy(t => t.Name).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Team>> GetBySeasonAsync(Guid seasonId, CancellationToken cancellationToken = default)
    {
        var fromStandings = db.Standings.Where(s => s.SeasonId == seasonId).Select(s => s.TeamId);
        var homeTeams = db.Matches.Where(m => m.SeasonId == seasonId).Select(m => m.HomeTeamId);
        var awayTeams = db.Matches.Where(m => m.SeasonId == seasonId).Select(m => m.AwayTeamId);
        var ids = fromStandings.Union(homeTeams).Union(awayTeams);

        return await db.Teams
            .Where(t => ids.Contains(t.Id))
            .OrderBy(t => t.Name)
            .ToListAsync(cancellationToken);
    }

    public void Add(Team team) => db.Teams.Add(team);

    /// <summary>Plantilla visible: sin las fichas del proveedor que el club ha retirado.</summary>
    public async Task<IReadOnlyList<RosterEntry>> GetRosterAsync(Guid teamId, Guid seasonId, CancellationToken cancellationToken = default) =>
        await db.RosterEntries
            .Where(r => r.TeamId == teamId && r.SeasonId == seasonId && !r.Hidden)
            .ToListAsync(cancellationToken);

    public Task<RosterEntry?> GetRosterEntryAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.RosterEntries.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

    public void RemoveRosterEntry(RosterEntry entry) => db.RosterEntries.Remove(entry);

    public async Task<IReadOnlyList<RosterEntry>> GetSeasonRosterAsync(Guid seasonId, CancellationToken cancellationToken = default) =>
        await db.RosterEntries
            .Where(r => r.SeasonId == seasonId)
            .ToListAsync(cancellationToken);

    public void AddRosterEntry(RosterEntry entry) => db.RosterEntries.Add(entry);
}
