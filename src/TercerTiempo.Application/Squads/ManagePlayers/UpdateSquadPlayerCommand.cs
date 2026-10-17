using FluentValidation;
using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Common;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Players;
using TercerTiempo.Domain.Squads;

namespace TercerTiempo.Application.Squads.ManagePlayers;

public sealed record UpdateSquadPlayerCommand(
    Guid SquadId,
    Guid SquadPlayerId,
    string FullName,
    Position? Position,
    int? ShirtNumber) : ICommand<SquadPlayerDto>;

internal sealed class UpdateSquadPlayerValidator : AbstractValidator<UpdateSquadPlayerCommand>
{
    public UpdateSquadPlayerValidator()
    {
        RuleFor(x => x.SquadId).NotEmpty();
        RuleFor(x => x.SquadPlayerId).NotEmpty();
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Position).IsInEnum().When(x => x.Position.HasValue);
        RuleFor(x => x.ShirtNumber).InclusiveBetween(1, 99).When(x => x.ShirtNumber.HasValue);
    }
}

internal sealed class UpdateSquadPlayerHandler(
    ICurrentUser currentUser,
    ISquadStore squads,
    IPlayerStore players,
    IUnitOfWork unitOfWork) : ICommandHandler<UpdateSquadPlayerCommand, SquadPlayerDto>
{
    public async Task<SquadPlayerDto> Handle(UpdateSquadPlayerCommand command, CancellationToken cancellationToken)
    {
        var squad = await Resolvers.SquadForStaffAsync(squads, currentUser, command.SquadId, cancellationToken);

        squad.UpdatePlayer(command.SquadPlayerId, command.FullName, command.Position, command.ShirtNumber);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var player = squad.GetPlayer(command.SquadPlayerId);
        var linked = player.PlayerId is { } id
            ? (await players.GetByIdsAsync([id], cancellationToken)).ToDictionary(p => p.Id)
            : null;

        return SquadMapper.ToPlayerDto(player, linked);
    }
}
