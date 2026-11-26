using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Abstractions.Teams;
using TercerTiempo.Domain.Teams;

namespace TercerTiempo.Application.Teams.GetTeamCrest;

public sealed record GetTeamCrestQuery(Guid TeamId, int? Size = null) : IQuery<CrestFile?>;

internal sealed class GetTeamCrestHandler(ITeamStore teams, ICrestStorage crests, ICrestThumbnails thumbnails) : IQueryHandler<GetTeamCrestQuery, CrestFile?>
{
    public async Task<CrestFile?> Handle(GetTeamCrestQuery query, CancellationToken cancellationToken)
    {
        var team = await teams.GetByIdAsync(query.TeamId, cancellationToken);
        if (team is null || !team.HasVisibleCrest()) return null;

        var crestKey = team.CrestKey!;
        return query.Size is { } requested && CrestThumbnailSizes.Snap(requested) is { } size
            ? await thumbnails.GetAsync(crestKey, size, ct => crests.OpenReadAsync(crestKey, ct), cancellationToken)
            : await crests.OpenReadAsync(crestKey, cancellationToken);
    }
}
