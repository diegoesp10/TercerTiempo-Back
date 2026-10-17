using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Common;
using TercerTiempo.Domain.Competitions;
using TercerTiempo.Domain.Players;
using TercerTiempo.Domain.Teams;

namespace TercerTiempo.Application.Players.GetCurrentPlayer;

public sealed record GetCurrentPlayerQuery : IQuery<PlayerDto>;

internal sealed class GetCurrentPlayerHandler(
    ICurrentUser currentUser,
    IPlayerStore players,
    ICompetitionStore competitions,
    ITeamStore teams) : IQueryHandler<GetCurrentPlayerQuery, PlayerDto>
{
    public async Task<PlayerDto> Handle(GetCurrentPlayerQuery query, CancellationToken cancellationToken)
    {
        var player = await Resolvers.CurrentPlayerAsync(currentUser, players, cancellationToken);
        return await PlayerMapper.ToDtoAsync(player, competitions, teams, cancellationToken);
    }
}
