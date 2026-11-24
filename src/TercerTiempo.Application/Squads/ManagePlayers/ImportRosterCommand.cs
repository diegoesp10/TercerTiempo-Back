using FluentValidation;
using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Common;
using TercerTiempo.Domain.Clubs;
using TercerTiempo.Domain.Coaches;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Competitions;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.Performances;
using TercerTiempo.Domain.Players;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Teams;

namespace TercerTiempo.Application.Squads.ManagePlayers;

public sealed record ImportRosterCommand(Guid SquadId) : ICommand<SquadDetailDto>;

internal sealed class ImportRosterValidator : AbstractValidator<ImportRosterCommand>
{
    public ImportRosterValidator() => RuleFor(x => x.SquadId).NotEmpty();
}

internal sealed class ImportRosterHandler(
    ICurrentUser currentUser,
    ISquadStore squads,
    ICoachStore coaches,
    IPlayerStore players,
    ITeamStore teams,
    ICompetitionStore competitions,
    IInjuryStore injuries,
    IClubStore clubs,
    IPerformanceStore performances,
    IMatchStore matches,
    Injuries.InjuryCarryOver carryOver,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<ImportRosterCommand, SquadDetailDto>
{
    public async Task<SquadDetailDto> Handle(ImportRosterCommand command, CancellationToken cancellationToken)
    {
        var squad = await Resolvers.SquadForManagerAsync(squads, currentUser, clubs, command.SquadId, clock, cancellationToken);
        var completed = SquadOpening.ImportRoster(squad, await teams.GetRosterAsync(squad.TeamId, squad.SeasonId, cancellationToken), clock.GetUtcNow());
        await SquadRosterReconciliation.RecalculateRatingsAsync(squad, completed, performances, matches, cancellationToken);
        await carryOver.IntoAsync(squad, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return await SquadMapper.ToDetailAsync(squad, currentUser, clock, coaches, players, teams, competitions, injuries, clubs, cancellationToken);
    }
}
