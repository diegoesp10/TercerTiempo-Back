using System.ComponentModel;
using FluentValidation;
using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Auth;
using TercerTiempo.Application.Common;
using TercerTiempo.Domain.Common;

namespace TercerTiempo.Application.Clubs.ManageTeams;

/// <summary>El club vincula uno de los equipos publicados por el proveedor para poder nombrar a sus técnicos.</summary>
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
    public async Task<ClubDto> Handle(AddClubTeamCommand command, CancellationToken cancellationToken)
    {
        var club = await Resolvers.CurrentClubAsync(currentUser, stores.Clubs, cancellationToken);

        _ = await stores.Teams.GetByIdAsync(command.TeamId, cancellationToken)
            ?? throw new NotFoundException("Equipo", command.TeamId);

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
