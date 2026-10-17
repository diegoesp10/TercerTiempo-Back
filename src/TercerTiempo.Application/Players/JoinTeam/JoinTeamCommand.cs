using FluentValidation;
using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Common;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Competitions;
using TercerTiempo.Domain.Players;
using TercerTiempo.Domain.Teams;

namespace TercerTiempo.Application.Players.JoinTeam;

/// <summary>
/// El jugador se une a la plantilla de un equipo en la temporada activa.
/// Si indica una ficha de la plantilla scrapeada (RosterEntryId) o su nombre coincide con una,
/// la pertenencia nace verificada/activa; si no, queda pendiente de validación.
/// </summary>
public sealed record JoinTeamCommand(Guid TeamId, Guid? RosterEntryId, int? ShirtNumber) : ICommand<PlayerDto>;

internal sealed class JoinTeamValidator : AbstractValidator<JoinTeamCommand>
{
    public JoinTeamValidator()
    {
        RuleFor(x => x.TeamId).NotEmpty();
        RuleFor(x => x.ShirtNumber).InclusiveBetween(1, 99).When(x => x.ShirtNumber.HasValue);
    }
}

internal sealed class JoinTeamHandler(
    ICurrentUser currentUser,
    IPlayerStore players,
    ICompetitionStore competitions,
    ITeamStore teams,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<JoinTeamCommand, PlayerDto>
{
    public async Task<PlayerDto> Handle(JoinTeamCommand command, CancellationToken cancellationToken)
    {
        var player = await Resolvers.CurrentPlayerAsync(currentUser, players, cancellationToken);
        var (_, season) = await Resolvers.CurrentSeasonAsync(competitions, cancellationToken);

        var team = await teams.GetByIdAsync(command.TeamId, cancellationToken)
            ?? throw new NotFoundException("Equipo", command.TeamId);

        var roster = await teams.GetRosterAsync(team.Id, season.Id, cancellationToken);

        var entry = command.RosterEntryId is { } entryId
            ? roster.FirstOrDefault(r => r.Id == entryId) ?? throw new NotFoundException("Ficha de plantilla", entryId)
            : roster.FirstOrDefault(r => r.Matches(player.DisplayName));

        var membership = player.JoinSquad(
            team.Id, season.Id,
            command.ShirtNumber ?? entry?.ShirtNumber,
            verifiedInRoster: entry is not null,
            clock.GetUtcNow());

        // Copia del value object: EF no permite compartir la misma instancia owned entre dos agregados.
        if (entry?.ExternalRef is { } externalRef)
            player.LinkExternalRef(ExternalRef.Create(externalRef.Provider, externalRef.ExternalId));

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return PlayerMapper.ToDto(player, membership, team, season);
    }
}
