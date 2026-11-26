using Microsoft.EntityFrameworkCore;
using TercerTiempo.Domain.Squads;

namespace TercerTiempo.Infrastructure.Persistence.Stores;

internal sealed class SquadStore(AppDbContext db) : ISquadStore
{
    // Como en ClubStore: la puerta de licencia lee la plantilla antes que el handler, y cada lectura son tres consultas
    // (plantilla, cuerpo técnico y fichas). Con seguimiento sirve la que ya tiene el contexto; en las lecturas sin
    // seguimiento, la que se leyó durante el ámbito.
    private readonly Dictionary<Guid, Squad?> _readById = [];
    private readonly Dictionary<(Guid, Guid?, Guid?, Guid?, DateOnly), SquadAccessRow?> _access = [];

    private bool Reading => db.ChangeTracker.QueryTrackingBehavior != QueryTrackingBehavior.TrackAll;

    public async Task<Squad?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (db.Squads.Local.FindEntry(id) is { State: not EntityState.Detached } tracked) return tracked.Entity;
        if (!Reading) return await db.Squads.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (_readById.TryGetValue(id, out var cached)) return cached;
        return _readById[id] = await db.Squads.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
    }

    public Task<Squad?> GetByTeamAndSeasonAsync(Guid teamId, Guid seasonId, CancellationToken cancellationToken = default) =>
        db.Squads.FirstOrDefaultAsync(s => s.TeamId == teamId && s.SeasonId == seasonId, cancellationToken);

    public Task<Squad?> GetByInviteCodeAsync(string inviteCode, CancellationToken cancellationToken = default) =>
        db.Squads.FirstOrDefaultAsync(s => s.Players.Any(p => p.InviteCode == inviteCode && p.Status == SquadPlayerStatus.Active && p.PlayerId == null), cancellationToken);

    public async Task<IReadOnlyList<Squad>> GetByCoachAsync(Guid coachId, DateOnly today, CancellationToken cancellationToken = default) =>
        await db.Squads
            .Where(s => s.Staff.Any(m => m.CoachId == coachId && m.LeftAt == null && (m.ValidUntil == null || m.ValidUntil >= today)))
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<SquadRef>> GetRefsByCoachAsync(Guid coachId, DateOnly today, CancellationToken cancellationToken = default) =>
        await db.Squads.AsNoTracking()
            .Where(s => s.Staff.Any(m => m.CoachId == coachId && m.LeftAt == null && (m.ValidUntil == null || m.ValidUntil >= today)))
            .OrderByDescending(s => s.CreatedAt)
            .Select(s => new SquadRef(s.Id, s.TeamId, s.SeasonId, s.Visibility))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<SquadRef>> GetRefsBySeasonAsync(Guid seasonId, CancellationToken cancellationToken = default) =>
        await db.Squads.AsNoTracking()
            .Where(s => s.SeasonId == seasonId)
            .Select(s => new SquadRef(s.Id, s.TeamId, s.SeasonId, s.Visibility))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<SquadRef>> GetRefsAsync(IEnumerable<Guid> teamIds, IEnumerable<Guid> seasonIds, CancellationToken cancellationToken = default)
    {
        var teams = teamIds.Distinct().ToList();
        var seasons = seasonIds.Distinct().ToList();
        if (teams.Count == 0 || seasons.Count == 0) return [];
        return await db.Squads.AsNoTracking()
            .Where(s => teams.Contains(s.TeamId) && seasons.Contains(s.SeasonId))
            .Select(s => new SquadRef(s.Id, s.TeamId, s.SeasonId, s.Visibility))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Squad>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default)
    {
        var list = ids.Distinct().ToList();
        if (list.Count == 0) return [];
        return await db.Squads.Where(s => list.Contains(s.Id)).ToListAsync(cancellationToken);
    }

    public Task<Squad?> GetByPlayerAsync(Guid playerId, CancellationToken cancellationToken = default) =>
        db.Squads.FirstOrDefaultAsync(
            s => s.Players.Any(p => p.PlayerId == playerId && p.Status == SquadPlayerStatus.Active),
            cancellationToken);

    public async Task<IReadOnlyList<Squad>> GetAllByPlayerAsync(Guid playerId, CancellationToken cancellationToken = default) =>
        await db.Squads.Where(s => s.Players.Any(p => p.PlayerId == playerId)).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Squad>> GetBySeasonAsync(Guid seasonId, CancellationToken cancellationToken = default) =>
        await db.Squads.Where(s => s.SeasonId == seasonId).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<SquadPlayerName>> GetActivePlayerNamesAsync(CancellationToken cancellationToken = default) =>
        await db.Set<SquadPlayer>().AsNoTracking()
            .Where(p => p.Status == SquadPlayerStatus.Active)
            .Select(p => new SquadPlayerName(p.SquadId, p.Id, p.NormalizedName))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Squad>> GetWithPlayersMissingPositionAsync(CancellationToken cancellationToken = default) =>
        await db.Squads
            .Where(s => s.Players.Any(p => p.Status == SquadPlayerStatus.Active && p.Position == null))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Squad>> GetByTeamIdsAsync(IEnumerable<Guid> teamIds, Guid seasonId, CancellationToken cancellationToken = default)
    {
        var list = teamIds.Distinct().ToList();
        if (list.Count == 0) return [];

        return await db.Squads
            .Where(s => s.SeasonId == seasonId && list.Contains(s.TeamId))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Squad>> GetByTeamIdsAsync(IEnumerable<Guid> teamIds, CancellationToken cancellationToken = default)
    {
        var list = teamIds.Distinct().ToList();
        if (list.Count == 0) return [];

        return await db.Squads.Where(s => list.Contains(s.TeamId)).ToListAsync(cancellationToken);
    }

    // La puerta de licencia la pide antes que el handler: la segunda vez, la del ámbito.
    public async Task<SquadAccessRow?> GetAccessAsync(
        Guid squadId, Guid? coachId, Guid? playerId, Guid? clubId, DateOnly today, CancellationToken cancellationToken = default)
    {
        var key = (squadId, coachId, playerId, clubId, today);
        if (_access.TryGetValue(key, out var cached)) return cached;
        return _access[key] = await db.Squads.AsNoTracking()
            .Where(s => s.Id == squadId)
            .Select(s => new SquadAccessRow(
                s.Id, s.TeamId, s.SeasonId,
                coachId != null && s.Staff.Any(m => m.CoachId == coachId && m.LeftAt == null && (m.ValidUntil == null || m.ValidUntil >= today)),
                playerId != null && s.Players.Any(p => p.PlayerId == playerId && p.Status == SquadPlayerStatus.Active),
                clubId != null && db.ClubTeams.Any(t => t.ClubId == clubId && t.TeamId == s.TeamId)))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public void Add(Squad squad) => db.Squads.Add(squad);
}
