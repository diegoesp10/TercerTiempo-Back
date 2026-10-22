using TercerTiempo.Application.Performances;
using TercerTiempo.Application.Players;
using TercerTiempo.Application.Players.JoinSquad;
using TercerTiempo.Application.Squads;
using TercerTiempo.Application.Squads.Suggestions;
using TercerTiempo.Application.Stats;

namespace TercerTiempo.Repository.Players;

public interface IPlayersRepository
{
    Task<SquadInviteDto> GetInviteAsync(string inviteCode, CancellationToken cancellationToken = default);
    Task<PlayerDto> JoinSquadAsync(JoinSquadAsPlayerCommand command, CancellationToken cancellationToken = default);
    Task<PlayerDto> LeaveSquadAsync(CancellationToken cancellationToken = default);
    Task<PlayerDashboardDto> GetDashboardAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PerformanceWithMatchDto>> GetMyPerformancesAsync(CancellationToken cancellationToken = default);
    Task<PlayerDto> UploadPhotoAsync(Application.Clubs.ManageTeams.CrestUpload file, CancellationToken cancellationToken = default);
    Task<PlayerDto> DeletePhotoAsync(CancellationToken cancellationToken = default);
    Task<Application.Abstractions.Teams.CrestFile?> OpenPhotoAsync(Guid playerId, CancellationToken cancellationToken = default);
    Task<SuggestionDto> SuggestAsync(SuggestPerformanceCommand command, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SuggestionDto>> ListMySuggestionsAsync(CancellationToken cancellationToken = default);
}
