using TercerTiempo.Application.Auth;
using TercerTiempo.Application.Coaches.Photos;
using TercerTiempo.Application.Common;
using TercerTiempo.Application.Squads;
using TercerTiempo.Domain.Clubs;
using TercerTiempo.Domain.Coaches;
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
        var summaries = new List<SquadSummaryDto>(mine.Count);

        foreach (var squad in mine)
            summaries.Add(await SquadMapper.ToSummaryAsync(squad, coach.Id, stores.Clock, stores.Teams, stores.Competitions, cancellationToken));

        var appointments = new List<CoachAppointmentDto>();
        foreach (var appointment in (await stores.Appointments.GetByCoachAsync(coach.Id, cancellationToken)).OrderByDescending(a => a.ValidUntil))
        {
            var club = await stores.Clubs.GetByIdAsync(appointment.ClubId, cancellationToken);
            var team = await stores.Teams.GetByIdAsync(appointment.TeamId, cancellationToken);
            var (competition, season) = await Resolvers.SeasonByIdAsync(stores.Competitions, appointment.SeasonId, cancellationToken);
            var squad = mine.FirstOrDefault(s => s.TeamId == appointment.TeamId && s.SeasonId == appointment.SeasonId);

            appointments.Add(new CoachAppointmentDto(
                appointment.Id, club?.Name ?? "Club", appointment.TeamId, team?.Name ?? "Equipo",
                season.Id, season.Label, competition.Name, appointment.Role,
                appointment.ValidFrom, appointment.ValidUntil, appointment.StatusOn(today), squad?.Id));
        }

        return new CoachDto(coach.Id, coach.DisplayName, summaries, appointments, coach.PhotoLink());
    }
}
