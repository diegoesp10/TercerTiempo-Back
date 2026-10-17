using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Common;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Competitions;
using TercerTiempo.Domain.Players;

namespace TercerTiempo.Application.Players.LeaveTeam;

public sealed record LeaveTeamCommand : ICommand<PlayerDto>;

internal sealed class LeaveTeamHandler(
    ICurrentUser currentUser,
    IPlayerStore players,
    ICompetitionStore competitions,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<LeaveTeamCommand, PlayerDto>
{
    public async Task<PlayerDto> Handle(LeaveTeamCommand command, CancellationToken cancellationToken)
    {
        var player = await Resolvers.CurrentPlayerAsync(currentUser, players, cancellationToken);
        var (_, season) = await Resolvers.CurrentSeasonAsync(competitions, cancellationToken);

        player.LeaveSquad(season.Id, clock.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return PlayerMapper.ToDto(player, null, null, season);
    }
}
