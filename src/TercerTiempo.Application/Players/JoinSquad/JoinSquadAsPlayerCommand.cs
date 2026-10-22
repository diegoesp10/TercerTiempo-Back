using System.ComponentModel;
using FluentValidation;
using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Common;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Competitions;
using TercerTiempo.Domain.Players;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Teams;
using TercerTiempo.Domain.Common.Errors;

namespace TercerTiempo.Application.Players.JoinSquad;

public sealed record JoinSquadAsPlayerCommand(
    [property: Description("Código personal de un solo uso generado para tu ficha por el club o entrenador. El nombre de la cuenta debe coincidir con la ficha.")]
    string InviteCode) : ICommand<PlayerDto>;

internal sealed class JoinSquadAsPlayerValidator : AbstractValidator<JoinSquadAsPlayerCommand>
{
    public JoinSquadAsPlayerValidator() => RuleFor(x => x.InviteCode).NotEmpty().MaximumLength(20);
}

internal sealed class JoinSquadAsPlayerHandler(
    ICurrentUser currentUser,
    IPlayerStore players,
    ISquadStore squads,
    ITeamStore teams,
    ICompetitionStore competitions,
    IUnitOfWork unitOfWork) : ICommandHandler<JoinSquadAsPlayerCommand, PlayerDto>
{
    public async Task<PlayerDto> Handle(JoinSquadAsPlayerCommand command, CancellationToken cancellationToken)
    {
        var player = await Resolvers.CurrentPlayerAsync(currentUser, players, cancellationToken);

        if (await squads.GetByPlayerAsync(player.Id, cancellationToken) is not null)
            throw new ConflictException(ErrorCode.PlayerAlreadyInSquad);

        var squad = await squads.GetByInviteCodeAsync(InviteCodes.Normalize(command.InviteCode), cancellationToken)
            ?? throw new NotFoundException(ErrorCode.InviteCodeNotFound, command.InviteCode);

        var entry = squad.InvitedPlayer(command.InviteCode, player.DisplayName);

        squad.LinkPlayer(entry.Id, player.Id);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return await PlayerMapper.ToDtoAsync(player, squads, teams, competitions, cancellationToken);
    }
}
