using Microsoft.EntityFrameworkCore;
using TercerTiempo.Domain.Performances;

namespace TercerTiempo.Infrastructure.Persistence.Stores;

internal sealed class TeamMatchStatsStore(AppDbContext db) : ITeamMatchStatsStore
{
    public Task<TeamMatchStats?> GetAsync(Guid squadId, Guid matchId, CancellationToken cancellationToken = default) =>
        db.TeamMatchStats.FirstOrDefaultAsync(s => s.SquadId == squadId && s.MatchId == matchId, cancellationToken);

    public async Task<IReadOnlyList<TeamMatchStats>> GetBySquadAsync(Guid squadId, CancellationToken cancellationToken = default) =>
        await db.TeamMatchStats.Where(s => s.SquadId == squadId).ToListAsync(cancellationToken);

    public void Add(TeamMatchStats stats) => db.TeamMatchStats.Add(stats);
}
