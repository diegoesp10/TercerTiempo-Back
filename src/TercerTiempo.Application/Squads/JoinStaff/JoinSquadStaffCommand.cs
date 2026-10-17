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

namespace TercerTiempo.Application.Squads.JoinStaff;

/// <summary>Un técnico se incorpora al cuerpo técnico de una plantilla existente con su código de invitación.</summary>
public sealed record JoinSquadStaffCommand(
    [property: Description("Código de invitación de la plantilla.")]
    string InviteCode,
    [property: Description("Rol en el cuerpo técnico: HeadCoach, AssistantCoach, Delegate, Physio, Analyst u Other.")]
    StaffRole Role) : ICommand<SquadDetailDto>;

internal sealed class JoinSquadStaffValidator : AbstractValidator<JoinSquadStaffCommand>
{
    public JoinSquadStaffValidator()
    {
        RuleFor(x => x.InviteCode).NotEmpty().MaximumLength(20);
        RuleFor(x => x.Role).IsInEnum();
    }
}

internal sealed class JoinSquadStaffHandler(
    ICurrentUser currentUser,
    ICoachStore coaches,
    IPlayerStore players,
    ITeamStore teams,
    ICompetitionStore competitions,
    ISquadStore squads,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<JoinSquadStaffCommand, SquadDetailDto>
{
    public async Task<SquadDetailDto> Handle(JoinSquadStaffCommand command, CancellationToken cancellationToken)
    {
        var coach = await Resolvers.CurrentCoachAsync(currentUser, coaches, cancellationToken);

        var squad = await squads.GetByInviteCodeAsync(InviteCodes.Normalize(command.InviteCode), cancellationToken)
            ?? throw new NotFoundException("Código de invitación", command.InviteCode);

        squad.AddStaff(coach.Id, command.Role, clock.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return await SquadMapper.ToDetailAsync(squad, currentUser, coaches, players, teams, competitions, cancellationToken);
    }
}
