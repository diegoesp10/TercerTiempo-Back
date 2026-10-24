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

    public void Add(Competition competition) => db.Competitions.Add(competition);
}
