using System.ComponentModel;
using FluentValidation;
using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Auth;
using TercerTiempo.Application.Common;
using TercerTiempo.Application.OfficialSheets;
using TercerTiempo.Domain.Common;

namespace TercerTiempo.Application.Clubs.ManageTeams;

/// <summary>
/// El club vincula uno de los equipos publicados por el proveedor que no quedó vinculado al darse de alta (un filial
/// con otro nombre, un equipo nuevo). Solo equipos cuyo nombre se corresponde con el del club.
/// </summary>
public sealed record AddClubTeamCommand(
    [property: Description("Id del equipo (GET /api/teams?competitionId=...).")]
    Guid TeamId) : ICommand<ClubDto>;

internal sealed class AddClubTeamValidator : AbstractValidator<AddClubTeamCommand>
{
    public AddClubTeamValidator() => RuleFor(x => x.TeamId).NotEmpty();
}

internal sealed class AddClubTeamHandler(
    ICurrentUser currentUser,
    AccountStores stores,
    IUnitOfWork unitOfWork) : ICommandHandler<AddClubTeamCommand, ClubDto>
{
    /// <summary>Parecido mínimo entre el nombre del club y el del equipo (ver <see cref="TeamNameSimilarity"/>).</summary>
    private const double MinimumSimilarity = 0.5;

    public async Task<ClubDto> Handle(AddClubTeamCommand command, CancellationToken cancellationToken)
    {
        var club = await Resolvers.CurrentClubAsync(currentUser, stores.Clubs, cancellationToken);

        var team = await stores.Teams.GetByIdAsync(command.TeamId, cancellationToken)
            ?? throw new NotFoundException("Equipo", command.TeamId);

        if (TeamNameSimilarity.Score(club.Name, team.Name) < MinimumSimilarity)
            throw new DomainException($"{team.Name} no se corresponde con el nombre del club ({club.Name}).");

        if (await stores.Clubs.GetByTeamAsync(command.TeamId, cancellationToken) is { } owner && owner.Id != club.Id)
            throw new ConflictException("Ese equipo ya está vinculado a otro club.");

        club.AddTeam(command.TeamId, stores.Clock.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return await ClubMapper.ToDtoAsync(club, stores, cancellationToken);
    }
}

/// <summary>El club deja de gestionar un equipo (sus nombramientos ya emitidos no cambian).</summary>
public sealed record RemoveClubTeamCommand(Guid TeamId) : ICommand<ClubDto>;

internal sealed class RemoveClubTeamValidator : AbstractValidator<RemoveClubTeamCommand>
{
    public RemoveClubTeamValidator() => RuleFor(x => x.TeamId).NotEmpty();
}

internal sealed class RemoveClubTeamHandler(
    ICurrentUser currentUser,
    AccountStores stores,
    IUnitOfWork unitOfWork) : ICommandHandler<RemoveClubTeamCommand, ClubDto>
{
    public async Task<ClubDto> Handle(RemoveClubTeamCommand command, CancellationToken cancellationToken)
    {
        var club = await Resolvers.CurrentClubAsync(currentUser, stores.Clubs, cancellationToken);

        club.RemoveTeam(command.TeamId);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return await ClubMapper.ToDtoAsync(club, stores, cancellationToken);
    }
}
