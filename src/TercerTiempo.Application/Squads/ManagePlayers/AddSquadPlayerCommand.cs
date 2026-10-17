using System.ComponentModel;
using FluentValidation;
using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Common;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Players;
using TercerTiempo.Domain.Squads;

namespace TercerTiempo.Application.Squads.ManagePlayers;

/// <summary>El cuerpo técnico añade a mano un jugador a la plantilla (p. ej. cuando el proveedor no la publica).</summary>
public sealed record AddSquadPlayerCommand(
    Guid SquadId,
    [property: Description("Nombre y apellidos del jugador.")]
    string FullName,
    [property: Description("Opcional: Goalkeeper, Defender, Midfielder o Forward.")]
    Position? Position,
    [property: Description("Opcional, 1-99.")]
    int? ShirtNumber) : ICommand<SquadPlayerDto>;

internal sealed class AddSquadPlayerValidator : AbstractValidator<AddSquadPlayerCommand>
{
    public AddSquadPlayerValidator()
    {
        RuleFor(x => x.SquadId).NotEmpty();
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Position).IsInEnum().When(x => x.Position.HasValue);
        RuleFor(x => x.ShirtNumber).InclusiveBetween(1, 99).When(x => x.ShirtNumber.HasValue);
    }
}

internal sealed class AddSquadPlayerHandler(
    ICurrentUser currentUser,
    ISquadStore squads,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<AddSquadPlayerCommand, SquadPlayerDto>
{
    public async Task<SquadPlayerDto> Handle(AddSquadPlayerCommand command, CancellationToken cancellationToken)
    {
        var squad = await Resolvers.SquadForStaffAsync(squads, currentUser, command.SquadId, cancellationToken);

        var player = squad.AddPlayer(command.FullName, command.Position, command.ShirtNumber, null, clock.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return SquadMapper.ToPlayerDto(player, null);
    }
}
