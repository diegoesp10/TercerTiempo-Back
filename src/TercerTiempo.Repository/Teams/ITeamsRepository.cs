using TercerTiempo.Application.Abstractions.Teams;
using TercerTiempo.Application.Matches;
using TercerTiempo.Application.Teams;

namespace TercerTiempo.Repository.Teams;

public interface ITeamsRepository
{
    Task<IReadOnlyList<TeamSummaryDto>> ListAsync(Guid competitionId, CancellationToken cancellationToken = default);
    Task<TeamDetailDto> GetAsync(Guid teamId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MatchDto>> GetMatchesAsync(Guid teamId, CancellationToken cancellationToken = default);

    Task<CrestFile?> OpenCrestAsync(Guid teamId, CancellationToken cancellationToken = default);
    Task<CrestFile?> OpenRosterPhotoAsync(Guid teamId, Guid entryId, CancellationToken cancellationToken = default);
    Task<Application.Players.Card.PlayerCardDto> GetPlayerCardAsync(Guid teamId, Guid entryId, CancellationToken cancellationToken = default);
}
