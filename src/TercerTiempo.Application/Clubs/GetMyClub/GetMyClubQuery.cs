using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Auth;
using TercerTiempo.Application.Common;

namespace TercerTiempo.Application.Clubs.GetMyClub;

public sealed record GetMyClubQuery : IQuery<ClubDto>;

internal sealed class GetMyClubHandler(ICurrentUser currentUser, AccountStores stores) : IQueryHandler<GetMyClubQuery, ClubDto>
{
    public async Task<ClubDto> Handle(GetMyClubQuery query, CancellationToken cancellationToken)
    {
        var club = await Resolvers.CurrentClubAsync(currentUser, stores.Clubs, cancellationToken);
        return await ClubMapper.ToDtoAsync(club, stores, cancellationToken, withFilials: true);
    }
}
