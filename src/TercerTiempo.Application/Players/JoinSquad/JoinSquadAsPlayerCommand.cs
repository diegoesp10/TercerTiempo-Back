using System.ComponentModel;
using FluentValidation;
using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Common;
using TercerTiempo.Application.Players.Invites;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Competitions;
using TercerTiempo.Domain.Players;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Teams;
using TercerTiempo.Domain.Common.Errors;

namespace TercerTiempo.Application.Players.JoinSquad;

public sealed record JoinSquadAsPlayerCommand(
    [property: Description("Código personal de un solo uso generado para una ficha por el club o entrenador. La cuenta pasa a llamarse como esa ficha.")]
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

        var (squad, entry) = await InviteLookup.FindAsync(command.InviteCode, squads, cancellationToken);

        // El nombre de la cuenta es siempre el de la ficha: el jugador nunca lo escribe.
        squad.LinkPlayer(entry.Id, player.Id);
        player.UpdateProfile(entry.FullName, player.Position, player.BirthDate);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return await PlayerMapper.ToDtoAsync(player, squads, teams, competitions, cancellationToken);
    }
}
