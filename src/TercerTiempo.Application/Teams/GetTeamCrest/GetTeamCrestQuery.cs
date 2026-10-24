using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Abstractions.Teams;
using TercerTiempo.Domain.Teams;

namespace TercerTiempo.Application.Teams.GetTeamCrest;

public sealed record GetTeamCrestQuery(Guid TeamId) : IQuery<CrestFile?>;

internal sealed class GetTeamCrestHandler(ITeamStore teams, ICrestStorage crests) : IQueryHandler<GetTeamCrestQuery, CrestFile?>
{
    public async Task<CrestFile?> Handle(GetTeamCrestQuery query, CancellationToken cancellationToken)
    {
        var team = await teams.GetByIdAsync(query.TeamId, cancellationToken);
        return team is not null && team.HasVisibleCrest() ? await crests.OpenReadAsync(team.CrestKey!, cancellationToken) : null;
    }
}
