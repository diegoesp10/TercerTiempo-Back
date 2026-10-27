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
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<ImportRosterCommand, SquadDetailDto>
{
    public async Task<SquadDetailDto> Handle(ImportRosterCommand command, CancellationToken cancellationToken)
    {
        var squad = await Resolvers.SquadForManagerAsync(squads, currentUser, clubs, command.SquadId, clock, cancellationToken);
        var now = clock.GetUtcNow();

        foreach (var entry in await teams.GetRosterAsync(squad.TeamId, squad.SeasonId, cancellationToken))
        {
            // Las fichas con el mismo nombre añadidas a mano no se duplican.
            if (squad.ActivePlayers.Any(p => p.RosterEntryId == entry.Id || p.NormalizedName == entry.NormalizedName))
                continue;

            squad.AddPlayer(entry.FullName, entry.Position, entry.ShirtNumber, entry.Id, now);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return await SquadMapper.ToDetailAsync(squad, currentUser, clock, coaches, players, teams, competitions, injuries, clubs, cancellationToken);
    }
}
