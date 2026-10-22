using TercerTiempo.Application.Auth;
using TercerTiempo.Application.Common;
using TercerTiempo.Domain.Clubs;
using TercerTiempo.Domain.Squads;

using TercerTiempo.Application.Teams;

namespace TercerTiempo.Application.Clubs;

public sealed record ClubDto(
    Guid Id,
    string Name,
    IReadOnlyList<ClubTeamDto> Teams,
    IReadOnlyList<AppointmentDto> Appointments);

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
    int ActiveAppointments);

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
    public static async Task<ClubDto> ToDtoAsync(Club club, AccountStores stores, CancellationToken cancellationToken)
    {
        var today = SquadAccess.Today(stores.Clock);
        var appointments = await stores.Appointments.GetByClubAsync(club.Id, cancellationToken);

        var teamsDto = new List<ClubTeamDto>();
        foreach (var clubTeam in club.Teams.OrderBy(t => t.AddedAt))
        {
            var team = await stores.Teams.GetByIdAsync(clubTeam.TeamId, cancellationToken);
            if (team is null) continue;

            var competition = await stores.Competitions.GetByTeamAsync(team.Id, cancellationToken);
            var season = competition?.CurrentSeason;
            var squad = season is null ? null : await stores.Squads.GetByTeamAndSeasonAsync(team.Id, season.Id, cancellationToken);

            teamsDto.Add(new ClubTeamDto(
                team.Id, team.Name, team.ShortName, team.CrestLink(),
                competition?.Id, competition?.Name, season?.Id, season?.Label, season?.EndsOn,
                squad?.Id,
                appointments.Count(a => a.TeamId == team.Id && season is not null && a.SeasonId == season.Id && a.IsActiveOn(today))));
        }

        var appointmentsDto = new List<AppointmentDto>();
        foreach (var appointment in appointments.OrderByDescending(a => a.CreatedAt))
            appointmentsDto.Add(await ToDtoAsync(appointment, stores, today, cancellationToken));

        return new ClubDto(club.Id, club.Name, teamsDto, appointmentsDto);
    }

    public static async Task<AppointmentDto> ToDtoAsync(CoachAppointment appointment, AccountStores stores, DateOnly today, CancellationToken cancellationToken)
    {
        var team = await stores.Teams.GetByIdAsync(appointment.TeamId, cancellationToken);
        var (_, season) = await Resolvers.SeasonByIdAsync(stores.Competitions, appointment.SeasonId, cancellationToken);
        var coach = appointment.CoachId is { } coachId ? await stores.Coaches.GetByIdAsync(coachId, cancellationToken) : null;
        var status = appointment.StatusOn(today);

        return new AppointmentDto(
            appointment.Id, appointment.TeamId, team?.Name ?? "Equipo", season.Id, season.Label,
            appointment.CoachName, appointment.Email, appointment.Role,
            status == AppointmentStatus.Pending ? appointment.AccessCode : null,
            status, appointment.CoachId, coach?.DisplayName, appointment.RedeemedAt,
            appointment.ValidFrom, appointment.ValidUntil, appointment.CreatedAt);
    }
}
