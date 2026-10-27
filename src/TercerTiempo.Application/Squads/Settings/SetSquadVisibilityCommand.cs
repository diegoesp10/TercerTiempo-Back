using System.ComponentModel;
using FluentValidation;
using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Common;
using TercerTiempo.Domain.Clubs;
using TercerTiempo.Domain.Coaches;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Competitions;
using TercerTiempo.Domain.Players;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Teams;

namespace TercerTiempo.Application.Squads.Settings;

public sealed record SetSquadVisibilityCommand(
    Guid SquadId,
    [property: Description("Private (solo cuerpo técnico y jugadores vinculados) o Public (cualquiera).")]
    SquadVisibility Visibility) : ICommand<SquadDetailDto>;

internal sealed class SetSquadVisibilityValidator : AbstractValidator<SetSquadVisibilityCommand>
{
    public SetSquadVisibilityValidator()
    {
        RuleFor(x => x.SquadId).NotEmpty();
        RuleFor(x => x.Visibility).IsInEnum();
    }
}

internal sealed class SetSquadVisibilityHandler(
    ICurrentUser currentUser,
    ISquadStore squads,
    ICoachStore coaches,
    IPlayerStore players,
    ITeamStore teams,
    ICompetitionStore competitions,
    IInjuryStore injuries,
    IClubStore clubs,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<SetSquadVisibilityCommand, SquadDetailDto>
{
    public async Task<SquadDetailDto> Handle(SetSquadVisibilityCommand command, CancellationToken cancellationToken)
    {
        var squad = await Resolvers.SquadForManagerAsync(squads, currentUser, clubs, command.SquadId, clock, cancellationToken);

        squad.SetVisibility(command.Visibility);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return await SquadMapper.ToDetailAsync(squad, currentUser, clock, coaches, players, teams, competitions, injuries, clubs, cancellationToken);
    }
}
