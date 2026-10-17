using Microsoft.EntityFrameworkCore;
using TercerTiempo.Domain.Matches;

namespace TercerTiempo.Infrastructure.Persistence.Stores;

internal sealed class MatchStore(AppDbContext db) : IMatchStore
{
    public Task<Match?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.Matches.FirstOrDefaultAsync(m => m.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Match>> GetBySeasonAsync(Guid seasonId, CancellationToken cancellationToken = default) =>
        await db.Matches
            .Where(m => m.SeasonId == seasonId)
            .OrderBy(m => m.Matchday)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Match>> GetTeamMatchesAsync(Guid seasonId, Guid teamId, CancellationToken cancellationToken = default) =>
        await db.Matches
            .Where(m => m.SeasonId == seasonId && (m.HomeTeamId == teamId || m.AwayTeamId == teamId))
            .OrderBy(m => m.Matchday)
            .ToListAsync(cancellationToken);

    public void Add(Match match) => db.Matches.Add(match);
}
