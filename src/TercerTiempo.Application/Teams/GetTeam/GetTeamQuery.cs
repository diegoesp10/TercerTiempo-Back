using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Abstractions.Teams;
using TercerTiempo.Application.Common;
using TercerTiempo.Application.Competitions;
using TercerTiempo.Application.Standings;
using TercerTiempo.Domain.Competitions;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Standings;
using TercerTiempo.Domain.Teams;
using TercerTiempo.Domain.Common.Errors;

namespace TercerTiempo.Application.Teams.GetTeam;

/// <summary>Ficha pública del equipo: clasificación, plantilla del proveedor y si tiene plantilla gestionada en la plataforma.</summary>
public sealed record GetTeamQuery(Guid TeamId) : IQuery<TeamDetailDto>;

internal sealed class GetTeamHandler(
    ICurrentUser currentUser,
    ICompetitionStore competitions,
    ITeamStore teams,
    IStandingStore standings,
    ISquadStore squads,
    ITeamProfileProvider profiles,
    TimeProvider clock) : IQueryHandler<GetTeamQuery, TeamDetailDto>
{
    public async Task<TeamDetailDto> Handle(GetTeamQuery query, CancellationToken cancellationToken)
    {
        var team = await teams.GetByIdAsync(query.TeamId, cancellationToken)
            ?? throw new NotFoundException(ErrorCode.TeamNotFound, query.TeamId);

        var (competition, season) = await Resolvers.SeasonOfTeamAsync(competitions, team.Id, cancellationToken);

        var standing = (await standings.GetBySeasonAsync(season.Id, cancellationToken))
            .FirstOrDefault(s => s.TeamId == team.Id);

        var roster = (await teams.GetRosterAsync(team.Id, season.Id, cancellationToken))
            .OrderBy(r => r.ShirtNumber ?? int.MaxValue)
            .ThenBy(r => r.FullName)
            .Select(RosterEntryDto.From)
            .ToList();

        var squad = await squads.GetByTeamAndSeasonAsync(team.Id, season.Id, cancellationToken);
        var squadDto = squad is null
            ? null
            : new TeamSquadDto(
                squad.Id, squad.Visibility, squad.ActivePlayers.Count(), squad.StaffOn(SquadAccess.Today(clock)).Count(),
                SquadAccess.CanView(squad, currentUser, clock),
                SquadAccess.IsStaff(squad, currentUser, clock) || SquadAccess.IsMember(squad, currentUser));

        return new TeamDetailDto(
            team.Id, team.Name, team.ShortName, team.CrestLink(),
            SeasonDto.From(competition, season),
            standing is null ? null : StandingDto.From(standing, team),
            RosterPublished: roster.Count > 0,
            roster,
            squadDto,
            TeamProfileDto.From(team.Profile),
            team.OfficialRef is { } official && official.Provider == profiles.ProviderName ? profiles.TeamUrl(official.ExternalId) : null);
    }
}
