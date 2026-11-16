using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Abstractions.Teams;
using TercerTiempo.Application.Common;
using TercerTiempo.Domain.Clubs;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Teams;

namespace TercerTiempo.Application.Teams.GetRosterPhoto;

public sealed record GetRosterPhotoQuery(Guid TeamId, Guid EntryId) : IQuery<CrestFile?>;

internal sealed class GetRosterPhotoHandler(
    ITeamStore teams,
    IPlayerPhotoStorage photos,
    ICurrentUser currentUser,
    ISquadStore squads,
    IClubStore clubs,
    TimeProvider clock) : IQueryHandler<GetRosterPhotoQuery, CrestFile?>
{
    public async Task<CrestFile?> Handle(GetRosterPhotoQuery query, CancellationToken cancellationToken)
    {
        var entry = await teams.GetRosterEntryAsync(query.EntryId, cancellationToken);
        if (entry is not { PhotoKey: { } key, Hidden: false } || entry.TeamId != query.TeamId) return null;
        if (!await PhotoAccess.CanSeeTeamPhotosAsync(entry.TeamId, entry.SeasonId, currentUser, squads, clubs, clock, cancellationToken)) return null;
        return await photos.OpenReadAsync(key, cancellationToken);
    }
}
