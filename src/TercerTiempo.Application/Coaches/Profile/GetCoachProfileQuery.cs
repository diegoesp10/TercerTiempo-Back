using System.ComponentModel;
using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Auth;
using TercerTiempo.Application.Coaches.Photos;
using TercerTiempo.Application.Common;
using TercerTiempo.Application.Squads;
using TercerTiempo.Application.Teams;
using TercerTiempo.Domain.Clubs;
using TercerTiempo.Domain.Common.Errors;
using TercerTiempo.Domain.Squads;

namespace TercerTiempo.Application.Coaches.Profile;

public sealed record CoachProfileDto(
    Guid Id,
    string DisplayName,
    [property: Description("Foto que ha subido el propio técnico; null si no tiene.")]
    string? PhotoUrl,
    [property: Description("true si es el perfil de la cuenta que lo mira: solo entonces se puede cambiar la foto.")]
    bool IsMe,
    [property: Description("Equipos que dirige o ha dirigido con un nombramiento de su club, los vigentes primero.")]
    IReadOnlyList<CoachTeamDto> Teams);

public sealed record CoachTeamDto(
    Guid TeamId,
    string TeamName,
    string TeamShortName,
    string? TeamCrestUrl,
    string ClubName,
    string CompetitionName,
    string SeasonLabel,
    StaffRole Role,
    DateOnly ValidFrom,
    DateOnly ValidUntil,
    [property: Description("true mientras el nombramiento está vigente; false si ya caducó.")]
    bool Current,
    [property: Description("Plantilla de ese equipo en esa temporada (privada: solo la abren quienes tienen acceso).")]
    Guid? SquadId);

public sealed record GetCoachProfileQuery(Guid CoachId) : IQuery<CoachProfileDto>;

// Perfil de un técnico para cualquier cuenta: su foto y los equipos que dirige (y los que dirigió) por los nombramientos
// de sus clubes. Los revocados no salen: el club le quitó el acceso antes de tiempo.
internal sealed class GetCoachProfileHandler(ICurrentUser currentUser, AccountStores stores) : IQueryHandler<GetCoachProfileQuery, CoachProfileDto>
{
    public async Task<CoachProfileDto> Handle(GetCoachProfileQuery query, CancellationToken cancellationToken)
    {
        var coach = await stores.Coaches.GetByIdAsync(query.CoachId, cancellationToken)
            ?? throw new NotFoundException(ErrorCode.AccountNotFound, query.CoachId);
        var today = SquadAccess.Today(stores.Clock);

        var teams = new List<CoachTeamDto>();
        foreach (var appointment in await stores.Appointments.GetByCoachAsync(coach.Id, cancellationToken))
        {
            var status = appointment.StatusOn(today);
            if (status is not (AppointmentStatus.Active or AppointmentStatus.Expired)) continue;

            var team = await stores.Teams.GetByIdAsync(appointment.TeamId, cancellationToken);
            var club = await stores.Clubs.GetByIdAsync(appointment.ClubId, cancellationToken);
            var (competition, season) = await Resolvers.SeasonByIdAsync(stores.Competitions, appointment.SeasonId, cancellationToken);
            var squad = await stores.Squads.GetByTeamAndSeasonAsync(appointment.TeamId, appointment.SeasonId, cancellationToken);

            teams.Add(new CoachTeamDto(
                appointment.TeamId, team?.Name ?? "Equipo", team?.ShortName ?? string.Empty, team?.CrestLink(), club?.Name ?? "Club",
                competition.Name, season.Label, appointment.Role, appointment.ValidFrom, appointment.ValidUntil,
                status == AppointmentStatus.Active, squad?.Id));
        }

        return new CoachProfileDto(
            coach.Id,
            coach.DisplayName,
            coach.PhotoLink(),
            currentUser.CoachId == coach.Id,
            teams.OrderByDescending(t => t.Current).ThenByDescending(t => t.ValidUntil).ToList());
    }
}
