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

namespace TercerTiempo.Application.Players.JoinSquad;

/// <summary>
/// El jugador se vincula a su ficha en una plantilla con el código de invitación que le da el cuerpo técnico.
/// Si no indica ficha, se busca una no vinculada con su mismo nombre.
/// </summary>
public sealed record JoinSquadAsPlayerCommand(
    [property: Description("Código de invitación de la plantilla (lo facilita el cuerpo técnico).")]
    string InviteCode,
    [property: Description("Opcional: id de tu ficha en la plantilla (GET /api/players/me/invite/{code} devuelve las fichas sin vincular).")]
    Guid? SquadPlayerId) : ICommand<PlayerDto>;

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
            throw new ConflictException("Ya estás vinculado a una plantilla. Desvincúlate antes de unirte a otra.");

        var squad = await squads.GetByInviteCodeAsync(InviteCodes.Normalize(command.InviteCode), cancellationToken)
            ?? throw new NotFoundException("Código de invitación", command.InviteCode);

        var entry = command.SquadPlayerId is { } squadPlayerId
            ? squad.GetPlayer(squadPlayerId)
            : squad.ActivePlayers.FirstOrDefault(p => !p.IsLinked && p.Matches(player.DisplayName))
                ?? throw new ConflictException("No hay ninguna ficha con tu nombre: elige la tuya en la lista o pide al cuerpo técnico que te añada.");

        squad.LinkPlayer(entry.Id, player.Id);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return await PlayerMapper.ToDtoAsync(player, squads, teams, competitions, cancellationToken);
    }
}
