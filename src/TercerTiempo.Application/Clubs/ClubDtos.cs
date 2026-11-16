using System.ComponentModel;
using TercerTiempo.Application.Auth;
using TercerTiempo.Application.Clubs.Catalog;
using TercerTiempo.Application.Common;
using TercerTiempo.Domain.Clubs;
using TercerTiempo.Domain.Common.Errors;
using TercerTiempo.Domain.Squads;

using TercerTiempo.Application.Teams;

namespace TercerTiempo.Application.Clubs;

public sealed record ClubDto(
    Guid Id,
    string Name,
    [property: Description("Equipos que cubre la licencia del club: el primer equipo y sus filiales.")]
    IReadOnlyList<ClubTeamDto> Teams,
    IReadOnlyList<AppointmentDto> Appointments,
    [property: Description("Filiales del club (mismo club del catálogo) que aún no están vinculados y se pueden añadir a la licencia. Solo en GET /api/clubs/me y al vincular o desvincular.")]
    IReadOnlyList<ClubFilialDto> AvailableFilials);

public sealed record ClubFilialDto(Guid TeamId, string TeamName, string? TeamCrestUrl, string? CompetitionName);

public sealed record ClubTeamDto(
    Guid TeamId,
    string TeamName,
    string TeamShortName,
    string? TeamCrestUrl,
    Guid? CompetitionId,
    string? CompetitionName,
    Guid? SeasonId,
    string? SeasonLabel,
    DateOnly? SeasonEndsOn,
    Guid? SquadId,
    int ActiveAppointments,
    [property: Description("Estado de la licencia del club, que cubre a este equipo: Trial, Active o Expired.")]
    Domain.Licensing.LicenseStatus License,
    [property: Description("Último día con acceso a los datos privados del equipo.")]
    DateOnly LicenseValidUntil);

public sealed record AppointmentDto(
    Guid Id,
    Guid TeamId,
    string TeamName,
    Guid SeasonId,
    string SeasonLabel,
    string CoachName,
    string? Email,
    StaffRole Role,
    string? AccessCode,
    AppointmentStatus Status,
    Guid? CoachId,
    string? CoachDisplayName,
    DateTimeOffset? RedeemedAt,
    DateOnly ValidFrom,
    DateOnly ValidUntil,
    DateTimeOffset CreatedAt);

internal static class ClubMapper
{
    // El panel del club se arma con unas pocas consultas en bloque (equipos, ligas, plantillas, temporadas y técnicos de
    // todos los equipos y nombramientos a la vez) en lugar de varias por equipo y por nombramiento.
    public static async Task<ClubDto> ToDtoAsync(Club club, AccountStores stores, CancellationToken cancellationToken, bool withFilials = false)
    {
        var today = SquadAccess.Today(stores.Clock);
        var appointments = await stores.Appointments.GetByClubAsync(club.Id, cancellationToken);
        var license = await stores.Licenses.EnsureForClubAsync(club, cancellationToken);

        var year = await stores.Seasons.SelectedAsync(cancellationToken);
        var clubTeams = club.Teams.OrderBy(t => t.AddedAt).ToList();
        var teams = (await stores.Teams.GetByIdsAsync(clubTeams.Select(t => t.TeamId).Concat(appointments.Select(a => a.TeamId)), cancellationToken))
            .ToDictionary(t => t.Id);
        var managed = clubTeams.Where(t => teams.ContainsKey(t.TeamId)).Select(t => t.TeamId).ToList();
        var competitionByTeam = managed.Count == 0
            ? new Dictionary<Guid, Domain.Competitions.Competition>()
            : await stores.Competitions.GetByTeamsAsync(managed, year?.Id, cancellationToken);
        var seasonByTeam = managed
            .Select(id => (Id: id, Season: competitionByTeam.TryGetValue(id, out var c) ? Resolvers.SeasonFor(c, year) : null))
            .Where(x => x.Season is not null)
            .ToDictionary(x => x.Id, x => x.Season!);
        IReadOnlyList<SquadRef> squads = seasonByTeam.Count == 0
            ? []
            : (await stores.Squads.GetRefsAsync(seasonByTeam.Keys, seasonByTeam.Values.Select(s => s.Id), cancellationToken))
                .Where(s => seasonByTeam.TryGetValue(s.TeamId, out var season) && season.Id == s.SeasonId)
                .ToList();

        var teamsDto = new List<ClubTeamDto>();
        foreach (var clubTeam in clubTeams)
        {
            if (!teams.TryGetValue(clubTeam.TeamId, out var team)) continue;

            var competition = competitionByTeam.GetValueOrDefault(team.Id);
            var season = seasonByTeam.GetValueOrDefault(team.Id);
            var squad = season is null ? null : squads.FirstOrDefault(s => s.TeamId == team.Id);

            teamsDto.Add(new ClubTeamDto(
                team.Id, team.Name, team.ShortName, team.CrestLink(),
                competition?.Id, competition?.Name, season?.Id, season?.Label, season?.EndsOn,
                squad?.Id,
                appointments.Count(a => a.TeamId == team.Id && season is not null && a.SeasonId == season.Id && a.IsActiveOn(today)),
                license.StatusOn(today), license.ValidUntil));
        }

        var seasons = appointments.Count == 0
            ? new Dictionary<Guid, Domain.Competitions.Season>()
            : (await stores.Competitions.GetBySeasonIdsAsync(appointments.Select(a => a.SeasonId).Distinct(), cancellationToken))
                .SelectMany(c => c.Seasons)
                .ToDictionary(s => s.Id);
        var coachIds = appointments.Where(a => a.CoachId is not null).Select(a => a.CoachId!.Value).Distinct().ToList();
        var coaches = coachIds.Count == 0
            ? new Dictionary<Guid, Domain.Coaches.Coach>()
            : (await stores.Coaches.GetByIdsAsync(coachIds, cancellationToken)).ToDictionary(c => c.Id);

        var appointmentsDto = appointments
            .OrderByDescending(a => a.CreatedAt)
            .Select(a => ToDto(
                a, teams.GetValueOrDefault(a.TeamId),
                seasons.GetValueOrDefault(a.SeasonId) ?? throw new NotFoundException(ErrorCode.CompetitionNotFound, a.SeasonId),
                a.CoachId is { } coachId ? coaches.GetValueOrDefault(coachId) : null,
                today))
            .ToList();

        return new ClubDto(club.Id, club.Name, teamsDto, appointmentsDto, withFilials ? await AvailableFilialsAsync(club, stores, cancellationToken) : []);
    }

    private static async Task<IReadOnlyList<ClubFilialDto>> AvailableFilialsAsync(Club club, AccountStores stores, CancellationToken cancellationToken)
    {
        var entry = ClubCatalog.ForClub(await ClubCatalog.LoadAsync(stores, cancellationToken), club);
        var candidates = (entry?.Teams ?? []).Where(t => !club.Manages(t.Team.Id)).ToList();
        if (candidates.Count == 0) return [];

        // Los que ya tiene otro club, de una vez.
        var taken = (await stores.Clubs.GetByTeamsAsync(candidates.Select(t => t.Team.Id), cancellationToken))
            .SelectMany(c => c.Teams.Select(t => t.TeamId))
            .ToHashSet();
        return candidates
            .Where(t => !taken.Contains(t.Team.Id))
            .Select(t => new ClubFilialDto(t.Team.Id, t.Team.Name, t.Team.CrestLink(), t.CompetitionName))
            .ToList();
    }

    public static async Task<AppointmentDto> ToDtoAsync(CoachAppointment appointment, AccountStores stores, DateOnly today, CancellationToken cancellationToken)
    {
        var team = await stores.Teams.GetByIdAsync(appointment.TeamId, cancellationToken);
        var (_, season) = await Resolvers.SeasonByIdAsync(stores.Competitions, appointment.SeasonId, cancellationToken);
        var coach = appointment.CoachId is { } coachId ? await stores.Coaches.GetByIdAsync(coachId, cancellationToken) : null;
        return ToDto(appointment, team, season, coach, today);
    }

    private static AppointmentDto ToDto(
        CoachAppointment appointment, Domain.Teams.Team? team, Domain.Competitions.Season season, Domain.Coaches.Coach? coach, DateOnly today)
    {
        var status = appointment.StatusOn(today);

        return new AppointmentDto(
            appointment.Id, appointment.TeamId, team?.Name ?? "Equipo", season.Id, season.Label,
            appointment.CoachName, appointment.Email, appointment.Role,
            status == AppointmentStatus.Pending ? appointment.AccessCode : null,
            status, appointment.CoachId, coach?.DisplayName, appointment.RedeemedAt,
            appointment.ValidFrom, appointment.ValidUntil, appointment.CreatedAt);
    }
}
