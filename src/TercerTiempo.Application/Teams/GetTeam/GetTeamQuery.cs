using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Common;
using TercerTiempo.Application.Competitions;
using TercerTiempo.Application.Standings;
using TercerTiempo.Domain.Competitions;
using TercerTiempo.Domain.Players;
using TercerTiempo.Domain.Standings;
using TercerTiempo.Domain.Teams;

namespace TercerTiempo.Application.Teams.GetTeam;

/// <summary>Ficha del equipo: clasificación, plantilla del proveedor y jugadores registrados en la plataforma.</summary>
public sealed record GetTeamQuery(Guid TeamId) : IQuery<TeamDetailDto>;

internal sealed class GetTeamHandler(
    ICompetitionStore competitions,
    ITeamStore teams,
    IStandingStore standings,
    IPlayerStore players) : IQueryHandler<GetTeamQuery, TeamDetailDto>
{
    public async Task<TeamDetailDto> Handle(GetTeamQuery query, CancellationToken cancellationToken)
    {
        var team = await teams.GetByIdAsync(query.TeamId, cancellationToken)
            ?? throw new NotFoundException("Equipo", query.TeamId);

        var (competition, season) = await Resolvers.CurrentSeasonAsync(competitions, cancellationToken);

        var standing = (await standings.GetBySeasonAsync(season.Id, cancellationToken))
            .FirstOrDefault(s => s.TeamId == team.Id);

        var roster = await teams.GetRosterAsync(team.Id, season.Id, cancellationToken);
        var members = await players.GetBySquadAsync(team.Id, season.Id, cancellationToken);

        var rosterDtos = roster
            .OrderBy(r => r.ShirtNumber ?? int.MaxValue)
            .ThenBy(r => r.FullName)
            .Select(r => new RosterEntryDto(
                r.Id, r.FullName, r.Position, r.ShirtNumber,
                Claimed: members.Any(m =>
                    (m.ExternalRef is not null && m.ExternalRef == r.ExternalRef) || m.NormalizedName == r.NormalizedName)))
            .ToList();

        var memberDtos = members
            .Select(m =>
            {
                var membership = m.MembershipFor(season.Id);
                return new SquadMemberDto(m.Id, m.DisplayName, m.Position, membership?.ShirtNumber,
                    membership?.Status ?? MembershipStatus.Inactive);
            })
            .OrderBy(m => m.ShirtNumber ?? int.MaxValue)
            .ToList();

        return new TeamDetailDto(
            team.Id, team.Name, team.ShortName, team.CrestUrl,
            SeasonDto.From(competition, season),
            standing is null ? null : StandingDto.From(standing, team),
            rosterDtos,
            memberDtos);
    }
}
