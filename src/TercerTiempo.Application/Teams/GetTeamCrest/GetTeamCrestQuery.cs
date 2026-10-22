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
        return team?.CrestKey is { } key ? await crests.OpenReadAsync(key, cancellationToken) : null;
    }
}
