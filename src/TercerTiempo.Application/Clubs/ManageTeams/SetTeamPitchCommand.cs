using System.ComponentModel;
using FluentValidation;
using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Auth;
using TercerTiempo.Application.Common;
using TercerTiempo.Application.Teams;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Common.Errors;
using TercerTiempo.Domain.Teams;

namespace TercerTiempo.Application.Clubs.ManageTeams;

// Medidas del terreno de juego del equipo, que no publica la federación: con ellas el analizador de vídeo calibra el
// campo de los partidos que se juegan en él (sin ellas usa un campo genérico de 120 × 70).
public sealed record SetTeamPitchCommand(
    [property: Description("Equipo del club.")]
    Guid TeamId,
    [property: Description("Largo en metros (60-130); null con el ancho también a null para quitar las medidas.")]
    decimal? LengthM,
    [property: Description("Ancho en metros (35-90).")]
    decimal? WidthM) : ICommand<TeamProfileDto>;

internal sealed class SetTeamPitchValidator : AbstractValidator<SetTeamPitchCommand>
{
    public SetTeamPitchValidator()
    {
        RuleFor(x => x.TeamId).NotEmpty();
        RuleFor(x => x).Must(x => (x.LengthM is null) == (x.WidthM is null))
            .WithName("lengthM")
            .WithCatalogMessage(ErrorCode.TeamPitchIncomplete);
        RuleFor(x => x.LengthM).InclusiveBetween(TeamProfile.MinPitchLengthM, TeamProfile.MaxPitchLengthM).When(x => x.LengthM.HasValue)
            .WithName("El largo del campo");
        RuleFor(x => x.WidthM).InclusiveBetween(TeamProfile.MinPitchWidthM, TeamProfile.MaxPitchWidthM).When(x => x.WidthM.HasValue)
            .WithName("El ancho del campo");
    }
}

internal sealed class SetTeamPitchHandler(
    ICurrentUser currentUser,
    AccountStores stores,
    IUnitOfWork unitOfWork) : ICommandHandler<SetTeamPitchCommand, TeamProfileDto>
{
    public async Task<TeamProfileDto> Handle(SetTeamPitchCommand command, CancellationToken cancellationToken)
    {
        var club = await Resolvers.CurrentClubAsync(currentUser, stores.Clubs, cancellationToken);
        if (!club.Manages(command.TeamId))
            throw new ForbiddenException(ErrorCode.TeamPitchOwnTeamsOnly);

        var team = await stores.Teams.GetByIdAsync(command.TeamId, cancellationToken)
            ?? throw new NotFoundException(ErrorCode.TeamNotFound, command.TeamId);

        team.SetPitch(command.LengthM, command.WidthM);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return TeamProfileDto.From(team.Profile) ?? TeamProfileDto.Empty;
    }
}
