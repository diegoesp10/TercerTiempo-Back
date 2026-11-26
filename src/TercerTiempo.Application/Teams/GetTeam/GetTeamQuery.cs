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
    Squads.Suspensions.StaffDisciplineReader staffDiscipline,
    PublicDataCache cache,
    TimeProvider clock) : IQueryHandler<GetTeamQuery, TeamDetailDto>
{
    public async Task<TeamDetailDto> Handle(GetTeamQuery query, CancellationToken cancellationToken)
    {
        var team = await teams.GetByIdAsync(query.TeamId, cancellationToken)
            ?? throw new NotFoundException(ErrorCode.TeamNotFound, query.TeamId);

        var (competition, season) = await Resolvers.SeasonOfTeamAsync(competitions, seasons, team.Id, cancellationToken);

        // Lo público de la ficha (clasificación, fichas con sus números y cuerpo técnico de las actas) es igual para todas
        // las cuentas, salvo lo del cuerpo técnico que solo se enseña con sesión: se guarda por equipo y temporada hasta que
        // cambia algo de lo que sale. Lo de cada cuenta (fotos, plantilla) se pone después.
        var signedIn = currentUser.IsAuthenticated;
        var shared = await cache.GetAsync(
            $"team:{team.Id:N}:{season.Id:N}:{(signedIn ? "sesion" : "publico")}",
            [PublicTags.Structure, PublicTags.Standings(season.Id), PublicTags.Matches(season.Id), PublicTags.Rosters, PublicTags.Sheets, PublicTags.Staff, PublicTags.Committee],
            ct => PublicAsync(team, competition, season, signedIn, ct),
            cancellationToken);

        var squad = await squads.GetByTeamAndSeasonAsync(team.Id, season.Id, cancellationToken);

        // La ficha del equipo es pública, las fotos de los jugadores no: el enlace solo va a quien puede abrirlo.
        var roster = shared.Roster;
        if (!await PhotoAccess.CanSeeTeamPhotosAsync(team.Id, squad, currentUser, clubs, clock, cancellationToken))
            roster = roster.Select(r => r with { PhotoUrl = null }).ToList();
        var squadDto = squad is null
            ? null
            : new TeamSquadDto(
                squad.Id, squad.Visibility, squad.ActivePlayers.Count(), squad.StaffOn(SquadAccess.Today(clock)).Count(),
                await SquadAccess.CanViewAsync(squad, currentUser, clock, clubs, cancellationToken),
                await SquadAccess.IsInsiderAsync(squad, currentUser, clock, clubs, cancellationToken));

        return shared with { Roster = roster, Squad = squadDto };
    }

    private async Task<TeamDetailDto> PublicAsync(Team team, Competition competition, Season season, bool signedIn, CancellationToken cancellationToken)
    {
        var standing = await standings.GetByTeamAsync(season.Id, team.Id, cancellationToken);
        // Los partidos del equipo se leen una vez: los usan las fichas, el cuerpo técnico y sus sanciones.
        var seasonMatches = await matches.GetTeamMatchesAsync(season.Id, team.Id, cancellationToken);
        var roster = await RosterSummary.ListAsync(team.Id, season.Id, seasonMatches, teams, sheets, cancellationToken);

        return new TeamDetailDto(
            team.Id, team.Name, team.ShortName, team.CrestLink(),
            SeasonDto.From(competition, season),
            standing is null ? null : StandingDto.From(standing, team),
            RosterPublished: roster.Count > 0,
            roster,
            Squad: null,
            TeamProfileDto.From(team.Profile),
            team.OfficialRef is { } official && official.Provider == profiles.ProviderName ? profiles.TeamUrl(official.ExternalId) : null,
            await StaffAsync(team.Id, season.Id, seasonMatches, signedIn, cancellationToken),
            SourceUrl(team.ExternalRef));
    }

    // Lo que dicen las actas oficiales es público; el perfil de cada uno, su función en la app y a quién más ha nombrado el
    // club, solo para cuentas con sesión (como el perfil del técnico y el buscador).
    private async Task<TeamStaffDto> StaffAsync(Guid teamId, Guid seasonId, IReadOnlyList<Match> season, bool signedIn, CancellationToken cancellationToken)
    {
        var byMatch = season
            .Where(m => m.KickoffAt <= clock.GetUtcNow())
            .ToDictionary(m => m.Id);
        var staffSheets = await sheets.GetStaffByMatchIdsAsync(byMatch.Keys, cancellationToken);
        var rows = new List<SheetStaffEntry>();
        foreach (var sheet in staffSheets)
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

        var discipline = rows.Count == 0 ? null : await staffDiscipline.LoadAsync(teamId, seasonId, season, staffSheets, cancellationToken);
        return TeamStaffSummary.Build(rows, appointed, includeAppOnly: signedIn, includeProfiles: signedIn, discipline);
    }

    // futbolasturiano.es lleva de /?p={id} a la página del equipo (/equipo/{slug}/).
    private static string? SourceUrl(ExternalRef? source) => source is { Provider: "futbolasturiano" }
        ? $"https://www.futbolasturiano.es/?p={Uri.EscapeDataString(source.ExternalId)}"
        : null;
}
