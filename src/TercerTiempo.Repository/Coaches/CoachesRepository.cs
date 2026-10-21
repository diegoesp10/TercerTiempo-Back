using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Coaches;
using TercerTiempo.Application.Coaches.Appointments;

namespace TercerTiempo.Repository.Coaches;

/// <summary>Lo que hace un técnico con su cuenta: canjear los códigos de acceso que le entrega su club.</summary>
public interface ICoachesRepository
{
    Task<CoachDto> RedeemAppointmentAsync(RedeemAppointmentCommand command, CancellationToken cancellationToken = default);
}

internal sealed class CoachesRepository(ICommandHandler<RedeemAppointmentCommand, CoachDto> redeem) : ICoachesRepository
{
    public Task<CoachDto> RedeemAppointmentAsync(RedeemAppointmentCommand command, CancellationToken cancellationToken = default) =>
        redeem.Handle(command, cancellationToken);
}
