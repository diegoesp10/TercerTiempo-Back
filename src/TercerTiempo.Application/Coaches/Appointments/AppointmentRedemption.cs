using TercerTiempo.Application.Licensing;
using TercerTiempo.Application.Common;
using TercerTiempo.Application.Squads;
using TercerTiempo.Domain.Clubs;
using TercerTiempo.Domain.Coaches;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Teams;
using TercerTiempo.Domain.Common.Errors;

namespace TercerTiempo.Application.Coaches.Appointments;

internal sealed class AppointmentRedemption(ICoachAppointmentStore appointments, ISquadStore squads, ITeamStore teams, ILicenseGate licenses)
{
    public async Task<CoachAppointment> FindRedeemableAsync(string accessCode, CancellationToken cancellationToken)
    {
        var appointment = await appointments.GetByAccessCodeAsync(AccessCodes.Normalize(accessCode), cancellationToken)
            ?? throw new NotFoundException(ErrorCode.AccessCodeNotFound, accessCode);

        if (appointment.IsRevoked)
            throw new ConflictException(ErrorCode.AppointmentRevoked);

        if (appointment.IsRedeemed)
            throw new ConflictException(ErrorCode.AppointmentAlreadyRedeemed);

        await licenses.EnsureTeamLicensedAsync(appointment.TeamId, cancellationToken);
        return appointment;
    }

    public async Task<Squad> RedeemAsync(CoachAppointment appointment, Coach coach, DateTimeOffset now, CancellationToken cancellationToken)
    {
        appointment.Redeem(coach.Id, now);

        var squad = await SquadOpening.OpenAsync(squads, teams, appointment.TeamId, appointment.SeasonId, now, cancellationToken);
        squad.AddStaff(coach.Id, appointment.Role, now, appointment.ValidUntil, appointment.Id);
        return squad;
    }
}
