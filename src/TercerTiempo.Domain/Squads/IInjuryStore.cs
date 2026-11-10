namespace TercerTiempo.Domain.Squads;

public interface IInjuryStore
{
    Task<PlayerInjury?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PlayerInjury>> GetBySquadAsync(Guid squadId, CancellationToken cancellationToken = default);
    void Add(PlayerInjury injury);
    void Remove(PlayerInjury injury);
}
