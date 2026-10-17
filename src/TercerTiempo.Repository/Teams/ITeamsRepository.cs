using TercerTiempo.Application.Matches;
using TercerTiempo.Application.Teams;

namespace TercerTiempo.Repository.Teams;

/// <summary>Equipos de la temporada activa, su ficha y su calendario.</summary>
public interface ITeamsRepository
{
    Task<IReadOnlyList<TeamSummaryDto>> ListAsync(CancellationToken cancellationToken = default);
    Task<TeamDetailDto> GetAsync(Guid teamId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MatchDto>> GetMatchesAsync(Guid teamId, CancellationToken cancellationToken = default);
}
