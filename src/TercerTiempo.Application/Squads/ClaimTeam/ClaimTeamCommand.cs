using System.ComponentModel;
using FluentValidation;
using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Common;
using TercerTiempo.Domain.Coaches;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Competitions;
using TercerTiempo.Domain.Players;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Teams;

namespace TercerTiempo.Application.Squads.ClaimTeam;

/// <summary>
/// Un técnico crea la plantilla de un equipo para la temporada en curso y se convierte en su primer entrenador.
/// Si el proveedor publica la plantilla del equipo, se importa como punto de partida.
/// Solo puede existir una plantilla por equipo y temporada: los demás técnicos entran con el código de invitación.
/// </summary>
public sealed record ClaimTeamCommand(
    [property: Description("Id del equipo (GET /api/teams?competitionId=...).")]
    Guid TeamId) : ICommand<SquadDetailDto>;

internal sealed class ClaimTeamValidator : AbstractValidator<ClaimTeamCommand>
{
    public ClaimTeamValidator() => RuleFor(x => x.TeamId).NotEmpty();
}

internal sealed class ClaimTeamHandler(
    ICurrentUser currentUser,
    ICoachStore coaches,
    IPlayerStore players,
    ITeamStore teams,
    ICompetitionStore competitions,
    ISquadStore squads,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<ClaimTeamCommand, SquadDetailDto>
{
    public async Task<SquadDetailDto> Handle(ClaimTeamCommand command, CancellationToken cancellationToken)
    {
        var coach = await Resolvers.CurrentCoachAsync(currentUser, coaches, cancellationToken);

        var team = await teams.GetByIdAsync(command.TeamId, cancellationToken)
            ?? throw new NotFoundException("Equipo", command.TeamId);

        var (_, season) = await Resolvers.SeasonOfTeamAsync(competitions, team.Id, cancellationToken);

        if (await squads.GetByTeamAndSeasonAsync(team.Id, season.Id, cancellationToken) is not null)
            throw new ConflictException("Este equipo ya tiene plantilla creada esta temporada. Pide el código de invitación a su cuerpo técnico.");

        var now = clock.GetUtcNow();
        var squad = Squad.Create(team.Id, season.Id, coach.Id, now);

        foreach (var entry in await teams.GetRosterAsync(team.Id, season.Id, cancellationToken))
            squad.AddPlayer(entry.FullName, entry.Position, entry.ShirtNumber, entry.Id, now);

        squads.Add(squad);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return await SquadMapper.ToDetailAsync(squad, currentUser, coaches, players, teams, competitions, cancellationToken);
    }
}
