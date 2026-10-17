using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Squads;
using TercerTiempo.Application.Squads.ClaimTeam;
using TercerTiempo.Application.Squads.GetSquad;
using TercerTiempo.Application.Squads.JoinStaff;
using TercerTiempo.Application.Squads.ListMySquads;
using TercerTiempo.Application.Squads.ManagePlayers;
using TercerTiempo.Application.Squads.Matches;
using TercerTiempo.Application.Squads.MatchSheet;
using TercerTiempo.Application.Squads.Settings;
using TercerTiempo.Application.Squads.Stats;

namespace TercerTiempo.Repository.Squads;

internal sealed class SquadsRepository(
    ICommandHandler<ClaimTeamCommand, SquadDetailDto> claimTeam,
    ICommandHandler<JoinSquadStaffCommand, SquadDetailDto> joinStaff,
    IQueryHandler<ListMySquadsQuery, IReadOnlyList<SquadSummaryDto>> listMine,
    IQueryHandler<GetSquadQuery, SquadDetailDto> getSquad,
    ICommandHandler<AddSquadPlayerCommand, SquadPlayerDto> addPlayer,
    ICommandHandler<UpdateSquadPlayerCommand, SquadPlayerDto> updatePlayer,
    ICommandHandler<RemoveSquadPlayerCommand, SquadPlayerDto> removePlayer,
    ICommandHandler<ImportRosterCommand, SquadDetailDto> importRoster,
    ICommandHandler<SetSquadVisibilityCommand, SquadDetailDto> setVisibility,
    ICommandHandler<RotateInviteCodeCommand, SquadDetailDto> rotateInviteCode,
    IQueryHandler<ListSquadMatchesQuery, IReadOnlyList<SquadMatchDto>> listMatches,
    IQueryHandler<GetMatchSheetQuery, MatchSheetDto> getMatchSheet,
    ICommandHandler<RecordMatchSheetCommand, MatchSheetDto> recordMatchSheet,
    IQueryHandler<GetSquadStatsQuery, SquadStatsDto> getStats) : ISquadsRepository
{
    public Task<SquadDetailDto> ClaimTeamAsync(Guid teamId, CancellationToken cancellationToken = default) =>
        claimTeam.Handle(new ClaimTeamCommand(teamId), cancellationToken);

    public Task<SquadDetailDto> JoinStaffAsync(JoinSquadStaffRequest request, CancellationToken cancellationToken = default) =>
        joinStaff.Handle(new JoinSquadStaffCommand(request.InviteCode, request.Role), cancellationToken);

    public Task<IReadOnlyList<SquadSummaryDto>> ListMineAsync(CancellationToken cancellationToken = default) =>
        listMine.Handle(new ListMySquadsQuery(), cancellationToken);

    public Task<SquadDetailDto> GetAsync(Guid squadId, CancellationToken cancellationToken = default) =>
        getSquad.Handle(new GetSquadQuery(squadId), cancellationToken);

    public Task<SquadPlayerDto> AddPlayerAsync(Guid squadId, SquadPlayerRequest request, CancellationToken cancellationToken = default) =>
        addPlayer.Handle(request.ToAdd(squadId), cancellationToken);

    public Task<SquadPlayerDto> UpdatePlayerAsync(Guid squadId, Guid squadPlayerId, SquadPlayerRequest request, CancellationToken cancellationToken = default) =>
        updatePlayer.Handle(request.ToUpdate(squadId, squadPlayerId), cancellationToken);

    public Task<SquadPlayerDto> RemovePlayerAsync(Guid squadId, Guid squadPlayerId, CancellationToken cancellationToken = default) =>
        removePlayer.Handle(new RemoveSquadPlayerCommand(squadId, squadPlayerId), cancellationToken);

    public Task<SquadDetailDto> ImportRosterAsync(Guid squadId, CancellationToken cancellationToken = default) =>
        importRoster.Handle(new ImportRosterCommand(squadId), cancellationToken);

    public Task<SquadDetailDto> SetVisibilityAsync(Guid squadId, SquadVisibilityRequest request, CancellationToken cancellationToken = default) =>
        setVisibility.Handle(new SetSquadVisibilityCommand(squadId, request.Visibility), cancellationToken);

    public Task<SquadDetailDto> RotateInviteCodeAsync(Guid squadId, CancellationToken cancellationToken = default) =>
        rotateInviteCode.Handle(new RotateInviteCodeCommand(squadId), cancellationToken);

    public Task<IReadOnlyList<SquadMatchDto>> ListMatchesAsync(Guid squadId, CancellationToken cancellationToken = default) =>
        listMatches.Handle(new ListSquadMatchesQuery(squadId), cancellationToken);

    public Task<MatchSheetDto> GetMatchSheetAsync(Guid squadId, Guid matchId, CancellationToken cancellationToken = default) =>
        getMatchSheet.Handle(new GetMatchSheetQuery(squadId, matchId), cancellationToken);

    public Task<MatchSheetDto> RecordMatchSheetAsync(Guid squadId, Guid matchId, MatchSheetRequest request, CancellationToken cancellationToken = default) =>
        recordMatchSheet.Handle(request.ToCommand(squadId, matchId), cancellationToken);

    public Task<SquadStatsDto> GetStatsAsync(Guid squadId, CancellationToken cancellationToken = default) =>
        getStats.Handle(new GetSquadStatsQuery(squadId), cancellationToken);
}
