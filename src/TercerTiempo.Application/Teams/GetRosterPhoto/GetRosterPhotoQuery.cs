using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Abstractions.Teams;
using TercerTiempo.Domain.Teams;

namespace TercerTiempo.Application.Teams.GetRosterPhoto;

public sealed record GetRosterPhotoQuery(Guid TeamId, Guid EntryId) : IQuery<CrestFile?>;

internal sealed class GetRosterPhotoHandler(ITeamStore teams, IPlayerPhotoStorage photos) : IQueryHandler<GetRosterPhotoQuery, CrestFile?>
{
    public async Task<CrestFile?> Handle(GetRosterPhotoQuery query, CancellationToken cancellationToken)
    {
        var entry = await teams.GetRosterEntryAsync(query.EntryId, cancellationToken);
        return entry is { PhotoKey: { } key } && entry.TeamId == query.TeamId ? await photos.OpenReadAsync(key, cancellationToken) : null;
    }
}
