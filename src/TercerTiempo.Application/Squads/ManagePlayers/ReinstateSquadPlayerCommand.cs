using FluentValidation;
using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Common;
using TercerTiempo.Domain.Clubs;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Common.Errors;
using TercerTiempo.Domain.Squads;

namespace TercerTiempo.Application.Squads.ManagePlayers;

public sealed record ReinstateSquadPlayerCommand(Guid SquadId, Guid SquadPlayerId) : ICommand<SquadPlayerDto>;

internal sealed class ReinstateSquadPlayerValidator : AbstractValidator<ReinstateSquadPlayerCommand>
{
    public ReinstateSquadPlayerValidator()
    {
        RuleFor(x => x.SquadId).NotEmpty();
        RuleFor(x => x.SquadPlayerId).NotEmpty();
    }
}

// Deshace una baja: la ficha vuelve con su historial y, si tenía la cuenta vinculada, el jugador recupera el acceso. No
// si con esa cuenta juega ya en otra plantilla (una cuenta, una plantilla).
internal sealed class ReinstateSquadPlayerHandler(
    ICurrentUser currentUser,
    ISquadStore squads,
    IClubStore clubs,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<ReinstateSquadPlayerCommand, SquadPlayerDto>
{
    public async Task<SquadPlayerDto> Handle(ReinstateSquadPlayerCommand command, CancellationToken cancellationToken)
    {
        var squad = await Resolvers.SquadForManagerAsync(squads, currentUser, clubs, command.SquadId, clock, cancellationToken);
        var entry = squad.GetPlayer(command.SquadPlayerId);

        if (!entry.IsActive && entry.PlayerId is { } playerId
            && await squads.GetByPlayerAsync(playerId, cancellationToken) is { } other && other.Id != squad.Id)
            throw new ConflictException(ErrorCode.SquadPlayerInOtherSquad, entry.FullName);

        var player = squad.ReinstatePlayer(command.SquadPlayerId);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return SquadMapper.ToPlayerDto(player, null);
    }
}
