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

public sealed record GeneratePlayerInviteCodeCommand(Guid SquadId, Guid SquadPlayerId) : ICommand<SquadDetailDto>;

internal sealed class GeneratePlayerInviteCodeValidator : AbstractValidator<GeneratePlayerInviteCodeCommand>
{
    public GeneratePlayerInviteCodeValidator()
    {
        RuleFor(x => x.SquadId).NotEmpty();
        RuleFor(x => x.SquadPlayerId).NotEmpty();
    }
}

internal sealed class GeneratePlayerInviteCodeHandler(
    ICurrentUser currentUser,
    ISquadStore squads,
    ICoachStore coaches,
    IPlayerStore players,
    ITeamStore teams,
    ICompetitionStore competitions,
    IInjuryStore injuries,
    IClubStore clubs,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<GeneratePlayerInviteCodeCommand, SquadDetailDto>
{
    public async Task<SquadDetailDto> Handle(GeneratePlayerInviteCodeCommand command, CancellationToken cancellationToken)
    {
        var squad = await Resolvers.SquadForManagerAsync(squads, currentUser, clubs, command.SquadId, clock, cancellationToken);

        squad.GeneratePlayerInviteCode(command.SquadPlayerId);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return await SquadMapper.ToDetailAsync(squad, currentUser, clock, coaches, players, teams, competitions, injuries, clubs, cancellationToken);
    }
}
