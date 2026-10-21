using FluentValidation;
using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Common;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Squads;

namespace TercerTiempo.Application.Squads.ManagePlayers;

/// <summary>Da de baja una ficha de la plantilla. Sus actas se conservan.</summary>
public sealed record RemoveSquadPlayerCommand(Guid SquadId, Guid SquadPlayerId) : ICommand<SquadPlayerDto>;

internal sealed class RemoveSquadPlayerValidator : AbstractValidator<RemoveSquadPlayerCommand>
{
    public RemoveSquadPlayerValidator()
    {
        RuleFor(x => x.SquadId).NotEmpty();
        RuleFor(x => x.SquadPlayerId).NotEmpty();
    }
}

internal sealed class RemoveSquadPlayerHandler(
    ICurrentUser currentUser,
    ISquadStore squads,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<RemoveSquadPlayerCommand, SquadPlayerDto>
{
    public async Task<SquadPlayerDto> Handle(RemoveSquadPlayerCommand command, CancellationToken cancellationToken)
    {
        var squad = await Resolvers.SquadForStaffAsync(squads, currentUser, command.SquadId, clock, cancellationToken);

        squad.RemovePlayer(command.SquadPlayerId, clock.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return SquadMapper.ToPlayerDto(squad.GetPlayer(command.SquadPlayerId), null);
    }
}
