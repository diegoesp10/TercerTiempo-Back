using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Domain.Clubs;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Competitions;
using TercerTiempo.Domain.Teams;

namespace TercerTiempo.Application.Clubs.Invitations;

public sealed record InvitationTeamDto(
    Guid TeamId,
    string Name,
    string? Competition,
    string? ClubName,
    ClubInvitationDto? Invitation);

public sealed record SearchInvitationTeamsQuery(string? Query) : IQuery<IReadOnlyList<InvitationTeamDto>>;

// Buscador del administrador al crear invitaciones: los equipos que coinciden con lo escrito, con su liga, el club que
// ya los gestiona y la invitación que tengan, para ver de un vistazo quién tiene una y quién no.
internal sealed class SearchInvitationTeamsHandler(
    ITeamStore teams,
    ICompetitionStore competitions,
    IClubStore clubs,
    IClubInvitationStore invitations,
    TimeProvider clock) : IQueryHandler<SearchInvitationTeamsQuery, IReadOnlyList<InvitationTeamDto>>
{
    public const int MinLength = 2;
    public const int MaxResults = 20;

    public async Task<IReadOnlyList<InvitationTeamDto>> Handle(SearchInvitationTeamsQuery query, CancellationToken cancellationToken)
    {
        var text = NameNormalizer.Normalize(query.Query);
        if (text.Length < MinLength) return [];

        var matches = (await teams.GetAllAsync(cancellationToken))
            .Select(t => (Team: t, Name: NameNormalizer.Normalize(t.Name), Short: NameNormalizer.Normalize(t.ShortName)))
            .Where(t => t.Name.Contains(text, StringComparison.Ordinal) || t.Short.Contains(text, StringComparison.Ordinal))
            .OrderBy(t => t.Name.StartsWith(text, StringComparison.Ordinal) || t.Short.StartsWith(text, StringComparison.Ordinal) ? 0 : 1)
            .ThenBy(t => t.Team.Name, StringComparer.CurrentCultureIgnoreCase)
            .Take(MaxResults)
            .Select(t => t.Team)
            .ToList();

        if (matches.Count == 0) return [];

        var now = clock.GetUtcNow();
        var leagues = await LeaguesAsync(matches.Select(t => t.Id).ToHashSet(), cancellationToken);
        var allInvitations = await invitations.GetAllAsync(cancellationToken);
        var result = new List<InvitationTeamDto>(matches.Count);

        foreach (var team in matches)
        {
            var club = await clubs.GetByTeamAsync(team.Id, cancellationToken);
            var normalized = NameNormalizer.Normalize(team.Name);

            // Las de texto libre de antes del buscador cuentan si su nota es el nombre exacto del equipo.
            var invitation = allInvitations
                .Where(i => i.TeamId == team.Id || (i.TeamId is null && NameNormalizer.Normalize(i.Note) == normalized))
                .OrderByDescending(i => i.IsUsable(now))
                .ThenByDescending(i => i.UsedAt is not null)
                .ThenByDescending(i => i.CreatedAt)
                .FirstOrDefault();

            Club? usedBy = invitation?.ClubId is { } clubId ? await clubs.GetByIdAsync(clubId, cancellationToken) : null;
            result.Add(new InvitationTeamDto(
                team.Id,
                team.Name,
                leagues.GetValueOrDefault(team.Id),
                club?.Name,
                invitation is null ? null : ClubInvitationMapper.ToDto(invitation, usedBy?.Name, now)));
        }

        return result;
    }

    private async Task<Dictionary<Guid, string>> LeaguesAsync(HashSet<Guid> teamIds, CancellationToken cancellationToken)
    {
        var result = new Dictionary<Guid, string>();
        foreach (var competition in (await competitions.GetAllAsync(cancellationToken)).OrderBy(c => c.SortOrder))
        {
            if (competition.CurrentSeason is not { } season) continue;
            foreach (var team in await teams.GetBySeasonAsync(season.Id, cancellationToken))
            {
                if (teamIds.Contains(team.Id)) result.TryAdd(team.Id, competition.Name);
            }
            if (result.Count == teamIds.Count) break;
        }
        return result;
    }
}
