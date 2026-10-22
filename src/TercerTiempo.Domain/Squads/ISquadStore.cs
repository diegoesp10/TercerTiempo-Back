namespace TercerTiempo.Domain.Squads;

public interface ISquadStore
{
    Task<Squad?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Squad?> GetByTeamAndSeasonAsync(Guid teamId, Guid seasonId, CancellationToken cancellationToken = default);
    Task<Squad?> GetByInviteCodeAsync(string inviteCode, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Squad>> GetByCoachAsync(Guid coachId, DateOnly today, CancellationToken cancellationToken = default);

    Task<Squad?> GetByPlayerAsync(Guid playerId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Squad>> GetBySeasonAsync(Guid seasonId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Squad>> GetByTeamIdsAsync(IEnumerable<Guid> teamIds, Guid seasonId, CancellationToken cancellationToken = default);

    void Add(Squad squad);
}
