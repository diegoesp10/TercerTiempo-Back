using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Performances;
using TercerTiempo.Application.Performances.ListMyPerformances;
using TercerTiempo.Application.Players;
using TercerTiempo.Application.Players.GetInvite;
using TercerTiempo.Application.Players.JoinSquad;
using TercerTiempo.Application.Players.LeaveSquad;
using TercerTiempo.Application.Squads;
using TercerTiempo.Application.Squads.Suggestions;
using TercerTiempo.Application.Stats;
using TercerTiempo.Application.Stats.GetPlayerDashboard;

namespace TercerTiempo.Repository.Players;

internal sealed class PlayersRepository(
    IQueryHandler<GetSquadInviteQuery, SquadInviteDto> getInvite,
    ICommandHandler<JoinSquadAsPlayerCommand, PlayerDto> joinSquad,
    ICommandHandler<LeaveSquadAsPlayerCommand, PlayerDto> leaveSquad,
    IQueryHandler<GetPlayerDashboardQuery, PlayerDashboardDto> getDashboard,
    IQueryHandler<ListMyPerformancesQuery, IReadOnlyList<PerformanceWithMatchDto>> listPerformances,
    ICommandHandler<Application.Players.Photos.UploadMyPhotoCommand, PlayerDto> uploadPhoto,
    ICommandHandler<Application.Players.Photos.DeleteMyPhotoCommand, PlayerDto> deletePhoto,
    IQueryHandler<Application.Players.Photos.OpenPlayerPhotoQuery, Application.Abstractions.Teams.CrestFile?> openPhoto,
    ICommandHandler<SuggestPerformanceCommand, SuggestionDto> suggest,
    IQueryHandler<ListMySuggestionsQuery, IReadOnlyList<SuggestionDto>> listSuggestions) : IPlayersRepository
{
    public Task<SquadInviteDto> GetInviteAsync(string inviteCode, CancellationToken cancellationToken = default) =>
        getInvite.Handle(new GetSquadInviteQuery(inviteCode), cancellationToken);

    public Task<PlayerDto> JoinSquadAsync(JoinSquadAsPlayerCommand command, CancellationToken cancellationToken = default) =>
        joinSquad.Handle(command, cancellationToken);

    public Task<PlayerDto> LeaveSquadAsync(CancellationToken cancellationToken = default) =>
        leaveSquad.Handle(new LeaveSquadAsPlayerCommand(), cancellationToken);

    public Task<PlayerDashboardDto> GetDashboardAsync(CancellationToken cancellationToken = default) =>
        getDashboard.Handle(new GetPlayerDashboardQuery(), cancellationToken);

    public Task<IReadOnlyList<PerformanceWithMatchDto>> GetMyPerformancesAsync(CancellationToken cancellationToken = default) =>
        listPerformances.Handle(new ListMyPerformancesQuery(), cancellationToken);

    public Task<PlayerDto> UploadPhotoAsync(Application.Clubs.ManageTeams.CrestUpload file, CancellationToken cancellationToken = default) =>
        uploadPhoto.Handle(new Application.Players.Photos.UploadMyPhotoCommand(file), cancellationToken);

    public Task<PlayerDto> DeletePhotoAsync(CancellationToken cancellationToken = default) =>
        deletePhoto.Handle(new Application.Players.Photos.DeleteMyPhotoCommand(), cancellationToken);

    public Task<Application.Abstractions.Teams.CrestFile?> OpenPhotoAsync(Guid playerId, CancellationToken cancellationToken = default) =>
        openPhoto.Handle(new Application.Players.Photos.OpenPlayerPhotoQuery(playerId), cancellationToken);

    public Task<SuggestionDto> SuggestAsync(SuggestPerformanceCommand command, CancellationToken cancellationToken = default) =>
        suggest.Handle(command, cancellationToken);

    public Task<IReadOnlyList<SuggestionDto>> ListMySuggestionsAsync(CancellationToken cancellationToken = default) =>
        listSuggestions.Handle(new ListMySuggestionsQuery(), cancellationToken);
}
