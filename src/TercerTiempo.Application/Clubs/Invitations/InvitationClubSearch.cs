using System.ComponentModel;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Auth;
using TercerTiempo.Application.Clubs.Catalog;
using TercerTiempo.Application.Common;
using TercerTiempo.Domain.Clubs;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Common.Errors;
using TercerTiempo.Domain.Squads;

namespace TercerTiempo.Application.Clubs.Invitations;

public sealed record InvitationClubDto(
    string Name,
    [property: Description("Primer equipo del club: es el que se manda en teamId al crear la invitación.")]
    Guid TeamId,
    IReadOnlyList<ClubCatalogTeamDto> Teams,
    [property: Description("Nombre de la cuenta si el club ya se ha dado de alta.")]
    string? RegisteredAs,
    ClubInvitationDto? Invitation);

public sealed record SearchInvitationClubsQuery(string? Query) : IQuery<IReadOnlyList<InvitationClubDto>>;

// Buscador del administrador al crear invitaciones: los clubes del catálogo (con sus equipos) que coinciden con lo
// escrito, si ya tienen cuenta y la invitación que tengan, para ver de un vistazo quién tiene una y quién no.
internal sealed class SearchInvitationClubsHandler(AccountStores stores, IClubInvitationStore invitations)
    : IQueryHandler<SearchInvitationClubsQuery, IReadOnlyList<InvitationClubDto>>
{
    public const int MinLength = 2;
    public const int MaxResults = 20;

    public async Task<IReadOnlyList<InvitationClubDto>> Handle(SearchInvitationClubsQuery query, CancellationToken cancellationToken)
    {
        var text = NameNormalizer.Normalize(query.Query);
        if (text.Length < MinLength) return [];

        bool Starts(ClubCatalogEntry e) =>
            e.NormalizedName.StartsWith(text, StringComparison.Ordinal) || e.Teams.Any(t => NameNormalizer.Normalize(t.Team.Name).StartsWith(text, StringComparison.Ordinal));

        var matches = (await ClubCatalog.LoadAsync(stores, cancellationToken))
            .Where(e => e.NormalizedName.Contains(text, StringComparison.Ordinal)
                || e.Teams.Any(t => NameNormalizer.Normalize(t.Team.Name).Contains(text, StringComparison.Ordinal)))
            .OrderBy(e => Starts(e) ? 0 : 1)
            .Take(MaxResults)
            .ToList();

        if (matches.Count == 0) return [];

        var now = stores.Clock.GetUtcNow();
        var allInvitations = await invitations.GetAllAsync(cancellationToken);
        var result = new List<InvitationClubDto>(matches.Count);

        foreach (var entry in matches)
        {
            var teamIds = entry.Teams.Select(t => t.Team.Id).ToHashSet();
            var names = entry.Teams.Select(t => NameNormalizer.Normalize(t.Team.Name)).Append(entry.NormalizedName).ToHashSet();
            var registered = await stores.Clubs.GetByNormalizedNameAsync(entry.NormalizedName, cancellationToken);

            // Las de texto libre de antes cuentan si su nota es el nombre del club o de uno de sus equipos.
            var invitation = allInvitations
                .Where(i => i.TeamId is { } teamId ? teamIds.Contains(teamId) : names.Contains(NameNormalizer.Normalize(i.Note)))
                .OrderByDescending(i => i.IsUsable(now))
                .ThenByDescending(i => i.UsedAt is not null)
                .ThenByDescending(i => i.CreatedAt)
                .FirstOrDefault();

            var usedBy = invitation?.ClubId is { } clubId ? await stores.Clubs.GetByIdAsync(clubId, cancellationToken) : null;
            result.Add(new InvitationClubDto(
                entry.Name,
                entry.Teams[0].Team.Id,
                entry.Teams.Select(t => new ClubCatalogTeamDto(t.Team.Id, t.Team.Name, t.CompetitionName)).ToList(),
                registered?.Name,
                invitation is null ? null : ClubInvitationMapper.ToDto(invitation, usedBy?.Name, now)));
        }

        return result;
    }
}

public sealed record ClubInvitationLookupDto(
    string Code,
    [property: Description("Club para el que es la invitación: en el alta va fijado y no se puede cambiar. null en las antiguas sin club.")]
    string? ClubName);

public sealed record GetClubInvitationQuery(string Code) : IQuery<ClubInvitationLookupDto>;

// Sin sesión, desde el alta: si el código vale y para qué club es. Las caducadas, usadas o anuladas dan 404.
internal sealed class GetClubInvitationHandler(AccountStores stores, IClubInvitationStore invitations)
    : IQueryHandler<GetClubInvitationQuery, ClubInvitationLookupDto>
{
    public async Task<ClubInvitationLookupDto> Handle(GetClubInvitationQuery query, CancellationToken cancellationToken)
    {
        var code = InviteCodes.Normalize(query.Code);
        var invitation = await invitations.GetByCodeAsync(code, cancellationToken);
        if (invitation is null || !invitation.IsUsable(stores.Clock.GetUtcNow()))
            throw new NotFoundException(ErrorCode.AuthClubInviteInvalid);

        var club = invitation.TeamId is { } teamId ? ClubCatalog.ForTeam(await ClubCatalog.LoadAsync(stores, cancellationToken), teamId) : null;
        return new ClubInvitationLookupDto(invitation.Code, club?.Name);
    }
}
