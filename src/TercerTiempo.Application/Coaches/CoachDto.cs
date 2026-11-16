using TercerTiempo.Application.Auth;
using TercerTiempo.Application.Coaches.Photos;
using TercerTiempo.Application.Common;
using TercerTiempo.Application.Squads;
using TercerTiempo.Domain.Clubs;
using TercerTiempo.Domain.Coaches;
using TercerTiempo.Domain.Common.Errors;
using TercerTiempo.Domain.Squads;

namespace TercerTiempo.Application.Coaches;

public sealed record CoachDto(
    Guid Id,
    string DisplayName,
    IReadOnlyList<SquadSummaryDto> Squads,
    IReadOnlyList<CoachAppointmentDto> Appointments,
    string? PhotoUrl = null);

public sealed record CoachAppointmentDto(
    Guid Id,
    string ClubName,
    Guid TeamId,
    string TeamName,
    Guid SeasonId,
    string SeasonLabel,
    string CompetitionName,
    StaffRole Role,
    DateOnly ValidFrom,
    DateOnly ValidUntil,
    AppointmentStatus Status,
    Guid? SquadId);

internal static class CoachMapper
{
    public static async Task<CoachDto> ToDtoAsync(Coach coach, AccountStores stores, CancellationToken cancellationToken)
    {
        var today = SquadAccess.Today(stores.Clock);
        var mine = await stores.Squads.GetByCoachAsync(coach.Id, today, cancellationToken);
        var summaries = await SquadMapper.ToSummariesAsync(mine, coach.Id, stores.Clock, stores.Teams, stores.Competitions, cancellationToken);

        // Clubes, equipos y ligas de todos los nombramientos en tres consultas.
        var list = (await stores.Appointments.GetByCoachAsync(coach.Id, cancellationToken)).OrderByDescending(a => a.ValidUntil).ToList();
        var clubMap = (await stores.Clubs.GetByIdsAsync(list.Select(a => a.ClubId), cancellationToken)).ToDictionary(c => c.Id);
        var teamMap = (await stores.Teams.GetByIdsAsync(list.Select(a => a.TeamId), cancellationToken)).ToDictionary(t => t.Id);
        var bySeason = (await stores.Competitions.GetBySeasonIdsAsync(list.Select(a => a.SeasonId), cancellationToken))
            .SelectMany(c => c.Seasons.Select(s => (Competition: c, Season: s)))
            .ToDictionary(x => x.Season.Id);

        var appointments = new List<CoachAppointmentDto>();
        foreach (var appointment in list)
        {
            var club = clubMap.GetValueOrDefault(appointment.ClubId);
            var team = teamMap.GetValueOrDefault(appointment.TeamId);
            if (!bySeason.TryGetValue(appointment.SeasonId, out var found))
                throw new NotFoundException(ErrorCode.CompetitionNotFound, appointment.SeasonId);
            var (competition, season) = found;
            var squad = mine.FirstOrDefault(s => s.TeamId == appointment.TeamId && s.SeasonId == appointment.SeasonId);

            appointments.Add(new CoachAppointmentDto(
                appointment.Id, club?.Name ?? "Club", appointment.TeamId, team?.Name ?? "Equipo",
                season.Id, season.Label, competition.Name, appointment.Role,
                appointment.ValidFrom, appointment.ValidUntil, appointment.StatusOn(today), squad?.Id));
        }

        return new CoachDto(coach.Id, coach.DisplayName, summaries, appointments, coach.PhotoLink());
    }
}
