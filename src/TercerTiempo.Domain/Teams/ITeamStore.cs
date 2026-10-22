namespace TercerTiempo.Domain.Teams;

public interface ITeamStore
{
    Task<Team?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Team>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Team>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Team>> GetBySeasonAsync(Guid seasonId, CancellationToken cancellationToken = default);

    void Add(Team team);

    Task<IReadOnlyList<RosterEntry>> GetRosterAsync(Guid teamId, Guid seasonId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RosterEntry>> GetSeasonRosterAsync(Guid seasonId, CancellationToken cancellationToken = default);
    Task<RosterEntry?> GetRosterEntryAsync(Guid id, CancellationToken cancellationToken = default);
    void AddRosterEntry(RosterEntry entry);
    void RemoveRosterEntry(RosterEntry entry);
}
