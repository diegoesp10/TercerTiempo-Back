using System.ComponentModel;
using FluentValidation;
using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Auth;
using TercerTiempo.Application.Common;
using TercerTiempo.Domain.Common;

namespace TercerTiempo.Application.Coaches.Appointments;

public sealed record RedeemAppointmentCommand(
    [property: Description("Código de acceso entregado por el club.")]
    string AccessCode) : ICommand<CoachDto>;

internal sealed class RedeemAppointmentValidator : AbstractValidator<RedeemAppointmentCommand>
{
    public RedeemAppointmentValidator() => RuleFor(x => x.AccessCode).NotEmpty().MaximumLength(20);
}

internal sealed class RedeemAppointmentHandler(
    ICurrentUser currentUser,
    AccountStores stores,
    AppointmentRedemption redemption,
    IUnitOfWork unitOfWork) : ICommandHandler<RedeemAppointmentCommand, CoachDto>
{
    public async Task<CoachDto> Handle(RedeemAppointmentCommand command, CancellationToken cancellationToken)
    {
        var coach = await Resolvers.CurrentCoachAsync(currentUser, stores.Coaches, cancellationToken);
        var appointment = await redemption.FindRedeemableAsync(command.AccessCode, cancellationToken);

        await redemption.RedeemAsync(appointment, coach, stores.Clock.GetUtcNow(), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return await CoachMapper.ToDtoAsync(coach, stores, cancellationToken);
    }
}
