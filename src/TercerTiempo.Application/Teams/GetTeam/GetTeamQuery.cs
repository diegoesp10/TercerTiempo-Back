using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Abstractions.Teams;
using TercerTiempo.Application.Common;
using TercerTiempo.Application.Competitions;
using TercerTiempo.Application.Standings;
using TercerTiempo.Application.Teams.Roster;
using TercerTiempo.Domain.Clubs;
using TercerTiempo.Domain.Coaches;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.OfficialSheets;
using TercerTiempo.Domain.Competitions;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Standings;
using TercerTiempo.Domain.Teams;
using TercerTiempo.Domain.Common.Errors;

namespace TercerTiempo.Application.Teams.GetTeam;

public sealed record GetTeamQuery(Guid TeamId) : IQuery<TeamDetailDto>;

internal sealed class GetTeamHandler(
    ICurrentUser currentUser,
    ICompetitionStore competitions,
    ITeamStore teams,
    IStandingStore standings,
    IMatchStore matches,
    IOfficialSheetStore sheets,
    ISquadStore squads,
    ITeamProfileProvider profiles,
    IClubStore clubs,
    ICoachAppointmentStore appointments,
    ICoachStore coaches,
    Seasons.ISeasonContext seasons,
    TimeProvider clock) : IQueryHandler<GetTeamQuery, TeamDetailDto>
{
    public async Task<TeamDetailDto> Handle(GetTeamQuery query, CancellationToken cancellationToken)
    {
        var team = await teams.GetByIdAsync(query.TeamId, cancellationToken)
            ?? throw new NotFoundException(ErrorCode.TeamNotFound, query.TeamId);

        var (competition, season) = await Resolvers.SeasonOfTeamAsync(competitions, seasons, team.Id, cancellationToken);

        var standing = await standings.GetByTeamAsync(season.Id, team.Id, cancellationToken);

        var roster = await RosterSummary.ListAsync(team.Id, season.Id, teams, matches, sheets, cancellationToken);

        var squad = await squads.GetByTeamAndSeasonAsync(team.Id, season.Id, cancellationToken);

        // La ficha del equipo es pública, las fotos de los jugadores no: el enlace solo va a quien puede abrirlo.
        if (!await PhotoAccess.CanSeeTeamPhotosAsync(team.Id, squad, currentUser, clubs, clock, cancellationToken))
            roster = roster.Select(r => r with { PhotoUrl = null }).ToList();
        var squadDto = squad is null
            ? null
            : new TeamSquadDto(
                squad.Id, squad.Visibility, squad.ActivePlayers.Count(), squad.StaffOn(SquadAccess.Today(clock)).Count(),
                await SquadAccess.CanViewAsync(squad, currentUser, clock, clubs, cancellationToken),
                await SquadAccess.IsInsiderAsync(squad, currentUser, clock, clubs, cancellationToken));

        return new TeamDetailDto(
            team.Id, team.Name, team.ShortName, team.CrestLink(),
            SeasonDto.From(competition, season),
            standing is null ? null : StandingDto.From(standing, team),
            RosterPublished: roster.Count > 0,
            roster,
            squadDto,
            TeamProfileDto.From(team.Profile),
            team.OfficialRef is { } official && official.Provider == profiles.ProviderName ? profiles.TeamUrl(official.ExternalId) : null,
            await StaffAsync(team.Id, season.Id, cancellationToken),
            SourceUrl(team.ExternalRef));
    }

    // Lo que dicen las actas oficiales es público; el perfil de cada uno, su función en la app y a quién más ha nombrado el
    // club, solo para cuentas con sesión (como el perfil del técnico y el buscador).
    private async Task<TeamStaffDto> StaffAsync(Guid teamId, Guid seasonId, CancellationToken cancellationToken)
    {
        var byMatch = (await matches.GetTeamMatchesAsync(seasonId, teamId, cancellationToken))
            .Where(m => m.KickoffAt <= clock.GetUtcNow())
            .ToDictionary(m => m.Id);
        var rows = new List<SheetStaffEntry>();
        foreach (var sheet in await sheets.GetStaffByMatchIdsAsync(byMatch.Keys, cancellationToken))
        {
            var match = byMatch[sheet.MatchId];
            var side = match.IsHome(teamId) ? MatchSide.Home : MatchSide.Away;
            rows.AddRange(sheet.StaffOf(side).Select(s => new SheetStaffEntry(sheet.Id, match.KickoffAt, s.Role, s.RoleLabel, s.FullName)));
        }

        var today = SquadAccess.Today(clock);
        var named = (await appointments.GetByTeamAndSeasonAsync(teamId, seasonId, cancellationToken))
            .Where(a => a.CoachId is not null && (a.RevokedAt is null || (a.RedeemedAt is { } redeemed && a.RevokedAt > redeemed)))
            .ToList();
        var coachProfiles = (await coaches.GetByIdsAsync(named.Select(a => a.CoachId!.Value).Distinct(), cancellationToken)).ToDictionary(c => c.Id);
        var appointed = named.Select(a =>
        {
            var coach = coachProfiles.GetValueOrDefault(a.CoachId!.Value);
            return new AppStaffEntry(
                a.CoachId!.Value, coach?.DisplayName ?? a.CoachName,
                new[] { coach?.NormalizedName, a.NormalizedCoachName }.OfType<string>().Distinct().ToList(),
                a.Role, a.IsActiveOn(today));
        }).ToList();

        return TeamStaffSummary.Build(rows, appointed, includeAppOnly: currentUser.IsAuthenticated, includeProfiles: currentUser.IsAuthenticated);
    }

    // futbolasturiano.es lleva de /?p={id} a la página del equipo (/equipo/{slug}/).
    private static string? SourceUrl(ExternalRef? source) => source is { Provider: "futbolasturiano" }
        ? $"https://www.futbolasturiano.es/?p={Uri.EscapeDataString(source.ExternalId)}"
        : null;
}
