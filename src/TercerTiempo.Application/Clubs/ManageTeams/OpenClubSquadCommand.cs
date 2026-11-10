using System.ComponentModel;
using FluentValidation;
using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Auth;
using TercerTiempo.Application.Common;
using TercerTiempo.Application.Squads;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Common.Errors;
using TercerTiempo.Domain.Matches;

namespace TercerTiempo.Application.Clubs.ManageTeams;

public sealed record OpenClubSquadCommand(
    Guid TeamId,
    [property: Description("Partido desde el que se abre: la plantilla es la de la temporada de ese partido. Vacío = la temporada que se está viendo.")]
    Guid? MatchId) : ICommand<ClubSquadDto>;

public sealed record ClubSquadDto(Guid SquadId, Guid TeamId, Guid SeasonId);

internal sealed class OpenClubSquadValidator : AbstractValidator<OpenClubSquadCommand>
{
    public OpenClubSquadValidator() => RuleFor(x => x.TeamId).NotEmpty();
}

// El club es la entidad de la que depende el cuerpo técnico: no necesita a un técnico para abrir la plantilla de su
// equipo y trabajar en ella (actas internas, datos del equipo, lesiones y vídeos).
internal sealed class OpenClubSquadHandler(
    ICurrentUser currentUser,
    AccountStores stores,
    IMatchStore matches,
    IUnitOfWork unitOfWork) : ICommandHandler<OpenClubSquadCommand, ClubSquadDto>
{
    public async Task<ClubSquadDto> Handle(OpenClubSquadCommand command, CancellationToken cancellationToken)
    {
        var club = await Resolvers.CurrentClubAsync(currentUser, stores.Clubs, cancellationToken);
        if (!club.Manages(command.TeamId))
            throw new ForbiddenException(ErrorCode.ClubTeamNotLinked);

        Guid seasonId;
        if (command.MatchId is { } matchId)
        {
            var match = await matches.GetByIdAsync(matchId, cancellationToken)
                ?? throw new NotFoundException(ErrorCode.MatchNotFound, matchId);
            if (!match.Involves(command.TeamId))
                throw new ConflictException(ErrorCode.SheetTeamNotInvolved);
            seasonId = match.SeasonId;
        }
        else
        {
            seasonId = (await Resolvers.SeasonOfTeamAsync(stores.Competitions, stores.Seasons, command.TeamId, cancellationToken)).Season.Id;
        }

        var squad = await SquadOpening.OpenAsync(stores.Squads, stores.Teams, command.TeamId, seasonId, stores.Clock.GetUtcNow(), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new ClubSquadDto(squad.Id, squad.TeamId, squad.SeasonId);
    }
}
