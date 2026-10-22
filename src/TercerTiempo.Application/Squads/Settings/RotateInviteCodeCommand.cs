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

namespace TercerTiempo.Application.Squads.Settings;

public sealed record RotateInviteCodeCommand(Guid SquadId) : ICommand<SquadDetailDto>;

internal sealed class RotateInviteCodeValidator : AbstractValidator<RotateInviteCodeCommand>
{
    public RotateInviteCodeValidator() => RuleFor(x => x.SquadId).NotEmpty();
}

internal sealed class RotateInviteCodeHandler(
    ICurrentUser currentUser,
    ISquadStore squads,
    ICoachStore coaches,
    IPlayerStore players,
    ITeamStore teams,
    ICompetitionStore competitions,
    IInjuryStore injuries,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<RotateInviteCodeCommand, SquadDetailDto>
{
    public async Task<SquadDetailDto> Handle(RotateInviteCodeCommand command, CancellationToken cancellationToken)
    {
        var squad = await Resolvers.SquadForStaffAsync(squads, currentUser, command.SquadId, clock, cancellationToken);

        squad.RotateInviteCode();
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return await SquadMapper.ToDetailAsync(squad, currentUser, clock, coaches, players, teams, competitions, injuries, cancellationToken);
    }
}
