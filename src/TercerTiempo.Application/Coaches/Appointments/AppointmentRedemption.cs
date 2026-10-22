using TercerTiempo.Application.Common;
using TercerTiempo.Domain.Clubs;
using TercerTiempo.Domain.Coaches;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Teams;
using TercerTiempo.Domain.Common.Errors;

namespace TercerTiempo.Application.Coaches.Appointments;

internal sealed class AppointmentRedemption(ICoachAppointmentStore appointments, ISquadStore squads, ITeamStore teams)
{
    public async Task<CoachAppointment> FindRedeemableAsync(string accessCode, CancellationToken cancellationToken)
    {
        var appointment = await appointments.GetByAccessCodeAsync(AccessCodes.Normalize(accessCode), cancellationToken)
            ?? throw new NotFoundException(ErrorCode.AccessCodeNotFound, accessCode);

        if (appointment.IsRevoked)
            throw new ConflictException(ErrorCode.AppointmentRevoked);

        if (appointment.IsRedeemed)
            throw new ConflictException(ErrorCode.AppointmentAlreadyRedeemed);

        return appointment;
    }

    public async Task<Squad> RedeemAsync(CoachAppointment appointment, Coach coach, DateTimeOffset now, CancellationToken cancellationToken)
    {
        appointment.Redeem(coach.Id, now);

        var squad = await squads.GetByTeamAndSeasonAsync(appointment.TeamId, appointment.SeasonId, cancellationToken);

        if (squad is null)
        {
            squad = Squad.Create(appointment.TeamId, appointment.SeasonId, coach.Id, now, appointment.Role, appointment.ValidUntil, appointment.Id);

            foreach (var entry in await teams.GetRosterAsync(appointment.TeamId, appointment.SeasonId, cancellationToken))
                squad.AddPlayer(entry.FullName, entry.Position, entry.ShirtNumber, entry.Id, now);

            squads.Add(squad);
        }
        else
        {
            squad.AddStaff(coach.Id, appointment.Role, now, appointment.ValidUntil, appointment.Id);
        }

        return squad;
    }
}
