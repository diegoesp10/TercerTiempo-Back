using Microsoft.EntityFrameworkCore;
using TercerTiempo.Domain.Squads;

namespace TercerTiempo.Infrastructure.Persistence.Stores;

internal sealed class SquadStore(AppDbContext db) : ISquadStore
{
    public Task<Squad?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.Squads.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

    public Task<Squad?> GetByTeamAndSeasonAsync(Guid teamId, Guid seasonId, CancellationToken cancellationToken = default) =>
        db.Squads.FirstOrDefaultAsync(s => s.TeamId == teamId && s.SeasonId == seasonId, cancellationToken);

    public Task<Squad?> GetByInviteCodeAsync(string inviteCode, CancellationToken cancellationToken = default) =>
        db.Squads.FirstOrDefaultAsync(s => s.InviteCode == inviteCode, cancellationToken);

    public async Task<IReadOnlyList<Squad>> GetByCoachAsync(Guid coachId, DateOnly today, CancellationToken cancellationToken = default)
    {
        var squads = await db.Squads
            .Where(s => s.Staff.Any(m => m.CoachId == coachId && m.LeftAt == null && (m.ValidUntil == null || m.ValidUntil >= today)))
            .ToListAsync(cancellationToken);

        // SQLite no admite ORDER BY sobre DateTimeOffset: se ordena en memoria (son pocas plantillas por técnico).
        return squads.OrderByDescending(s => s.CreatedAt).ToList();
    }

    public Task<Squad?> GetByPlayerAsync(Guid playerId, CancellationToken cancellationToken = default) =>
        db.Squads.FirstOrDefaultAsync(
            s => s.Players.Any(p => p.PlayerId == playerId && p.Status == SquadPlayerStatus.Active),
            cancellationToken);

    public async Task<IReadOnlyList<Squad>> GetBySeasonAsync(Guid seasonId, CancellationToken cancellationToken = default) =>
        await db.Squads.Where(s => s.SeasonId == seasonId).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Squad>> GetByTeamIdsAsync(IEnumerable<Guid> teamIds, Guid seasonId, CancellationToken cancellationToken = default)
    {
        var list = teamIds.Distinct().ToList();
        if (list.Count == 0) return [];

        return await db.Squads
            .Where(s => s.SeasonId == seasonId && list.Contains(s.TeamId))
            .ToListAsync(cancellationToken);
    }

    public void Add(Squad squad) => db.Squads.Add(squad);
}
