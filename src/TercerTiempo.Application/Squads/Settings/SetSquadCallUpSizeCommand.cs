using System.ComponentModel;
using FluentValidation;
using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Common;
using TercerTiempo.Domain.Clubs;
using TercerTiempo.Domain.Coaches;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Common.Errors;
using TercerTiempo.Domain.Competitions;
using TercerTiempo.Domain.Players;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Teams;

namespace TercerTiempo.Application.Squads.Settings;

public sealed record SetSquadCallUpSizeCommand(
    Guid SquadId,
    [property: Description("A cuántos se convoca siempre (de 7 a 30); null: se pregunta en cada convocatoria.")]
    int? Size) : ICommand<SquadDetailDto>;

internal sealed class SetSquadCallUpSizeValidator : AbstractValidator<SetSquadCallUpSizeCommand>
{
    public SetSquadCallUpSizeValidator()
    {
        RuleFor(x => x.SquadId).NotEmpty();
        RuleFor(x => x.Size!.Value).InclusiveBetween(Squad.MinCallUpSize, Squad.MaxCallUpSize).When(x => x.Size.HasValue)
            .WithCatalogMessage(ErrorCode.CallUpSizeInvalid, Squad.MinCallUpSize, Squad.MaxCallUpSize);
    }
}

// Las convocatorias que ya existen guardan su número hasta que se vuelvan a guardar: entonces manda el fijo.
internal sealed class SetSquadCallUpSizeHandler(
    ICurrentUser currentUser,
    ISquadStore squads,
    ICoachStore coaches,
    IPlayerStore players,
    ITeamStore teams,
    ICompetitionStore competitions,
    IInjuryStore injuries,
    IClubStore clubs,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<SetSquadCallUpSizeCommand, SquadDetailDto>
{
    public async Task<SquadDetailDto> Handle(SetSquadCallUpSizeCommand command, CancellationToken cancellationToken)
    {
        var squad = await Resolvers.SquadForManagerAsync(squads, currentUser, clubs, command.SquadId, clock, cancellationToken);

        squad.SetCallUpSize(command.Size);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return await SquadMapper.ToDetailAsync(squad, currentUser, clock, coaches, players, teams, competitions, injuries, clubs, cancellationToken);
    }
}
