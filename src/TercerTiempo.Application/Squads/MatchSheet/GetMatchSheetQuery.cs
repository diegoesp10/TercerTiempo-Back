using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Common;
using TercerTiempo.Domain.Competitions;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.Performances;
using TercerTiempo.Domain.Players;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Teams;

namespace TercerTiempo.Application.Squads.MatchSheet;

/// <summary>Acta de un partido de la plantilla, lista para consultar o rellenar.</summary>
public sealed record GetMatchSheetQuery(Guid SquadId, Guid MatchId) : IQuery<MatchSheetDto>;

internal sealed class GetMatchSheetHandler(
    ICurrentUser currentUser,
    ISquadStore squads,
    IMatchStore matches,
    ITeamStore teams,
    ICompetitionStore competitions,
    IPlayerStore players,
    IPerformanceStore performances) : IQueryHandler<GetMatchSheetQuery, MatchSheetDto>
{
    public async Task<MatchSheetDto> Handle(GetMatchSheetQuery query, CancellationToken cancellationToken)
    {
        var squad = await squads.GetByIdAsync(query.SquadId, cancellationToken)
            ?? throw new NotFoundException("Plantilla", query.SquadId);

        SquadAccess.EnsureCanView(squad, currentUser);

        var match = await matches.GetByIdAsync(query.MatchId, cancellationToken)
            ?? throw new NotFoundException("Partido", query.MatchId);

        if (match.SeasonId != squad.SeasonId || !match.Involves(squad.TeamId))
            throw new ConflictException("El partido no es de esta plantilla.");

        return await MatchSheetBuilder.BuildAsync(squad, match, currentUser, teams, competitions, players, performances, cancellationToken);
    }
}
