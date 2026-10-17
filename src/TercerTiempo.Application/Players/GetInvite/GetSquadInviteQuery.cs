using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Common;
using TercerTiempo.Application.Squads;
using TercerTiempo.Domain.Competitions;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Teams;

namespace TercerTiempo.Application.Players.GetInvite;

/// <summary>Qué plantilla hay detrás de un código de invitación y qué fichas siguen sin vincular (para que el jugador elija la suya).</summary>
public sealed record GetSquadInviteQuery(string InviteCode) : IQuery<SquadInviteDto>;

internal sealed class GetSquadInviteHandler(
    ICurrentUser currentUser,
    ISquadStore squads,
    ITeamStore teams,
    ICompetitionStore competitions) : IQueryHandler<GetSquadInviteQuery, SquadInviteDto>
{
    public async Task<SquadInviteDto> Handle(GetSquadInviteQuery query, CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated)
            throw new UnauthenticatedException();

        var squad = await squads.GetByInviteCodeAsync(InviteCodes.Normalize(query.InviteCode), cancellationToken)
            ?? throw new NotFoundException("Código de invitación", query.InviteCode);

        var team = await teams.GetByIdAsync(squad.TeamId, cancellationToken)
            ?? throw new NotFoundException("Equipo", squad.TeamId);

        var (competition, season) = await Resolvers.SeasonOfSquadAsync(competitions, squad, cancellationToken);

        var unlinked = squad.ActivePlayers
            .Where(p => !p.IsLinked)
            .OrderBy(p => p.ShirtNumber ?? int.MaxValue)
            .ThenBy(p => p.FullName)
            .Select(p => SquadMapper.ToPlayerDto(p, null))
            .ToList();

        return new SquadInviteDto(squad.Id, team.Name, competition.Name, season.Label, unlinked);
    }
}
