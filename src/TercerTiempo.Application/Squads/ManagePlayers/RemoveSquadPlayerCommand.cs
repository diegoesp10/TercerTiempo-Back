using FluentValidation;
using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Common;
using TercerTiempo.Domain.Alerts;
using TercerTiempo.Domain.Clubs;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Squads;

namespace TercerTiempo.Application.Squads.ManagePlayers;

public sealed record RemoveSquadPlayerCommand(Guid SquadId, Guid SquadPlayerId) : ICommand<SquadPlayerDto>;

internal sealed class RemoveSquadPlayerValidator : AbstractValidator<RemoveSquadPlayerCommand>
{
    public RemoveSquadPlayerValidator()
    {
        RuleFor(x => x.SquadId).NotEmpty();
        RuleFor(x => x.SquadPlayerId).NotEmpty();
    }
}

// La ficha se queda en la plantilla, en «De baja», con todo lo que hizo antes. Desde ese día ya no entra en nada nuevo
// (convocatorias, entrenamientos, actas) y los avisos que tenía abiertos de entrenamientos y convocatorias se cierran:
// enlazan a páginas que ya no puede abrir.
internal sealed class RemoveSquadPlayerHandler(
    ICurrentUser currentUser,
    ISquadStore squads,
    IClubStore clubs,
    IAlertStore alerts,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<RemoveSquadPlayerCommand, SquadPlayerDto>
{
    private static readonly AlertKind[] SquadAlerts = [AlertKind.TrainingCall, AlertKind.MatchCallUp, AlertKind.MatchNotCalledUp];

    public async Task<SquadPlayerDto> Handle(RemoveSquadPlayerCommand command, CancellationToken cancellationToken)
    {
        var squad = await Resolvers.SquadForManagerAsync(squads, currentUser, clubs, command.SquadId, clock, cancellationToken);
        var now = clock.GetUtcNow();

        squad.RemovePlayer(command.SquadPlayerId, now);
        var player = squad.GetPlayer(command.SquadPlayerId);
        if (player.PlayerId is { } playerId)
            foreach (var alert in await alerts.GetOpenByKeyPrefixAsync(string.Empty, [playerId], [], null, cancellationToken))
                if (SquadAlerts.Contains(alert.Kind))
                    alert.Resolve(now);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return SquadMapper.ToPlayerDto(player, null);
    }
}
