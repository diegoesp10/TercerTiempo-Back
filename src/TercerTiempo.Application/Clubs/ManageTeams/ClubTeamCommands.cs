using System.ComponentModel;
using FluentValidation;
using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Auth;
using TercerTiempo.Application.Clubs.Catalog;
using TercerTiempo.Application.Common;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Common.Errors;

namespace TercerTiempo.Application.Clubs.ManageTeams;

public sealed record AddClubTeamCommand(
    [property: Description("Id de un filial del club sin vincular (availableFilials de GET /api/clubs/me).")]
    Guid TeamId) : ICommand<ClubDto>;

internal sealed class AddClubTeamValidator : AbstractValidator<AddClubTeamCommand>
{
    public AddClubTeamValidator() => RuleFor(x => x.TeamId).NotEmpty();
}

// La licencia es del club y cubre su primer equipo y sus filiales: solo se vinculan equipos del mismo club del
// catálogo (los que se agrupan con él por el nombre), nunca otro club.
internal sealed class AddClubTeamHandler(
    ICurrentUser currentUser,
    AccountStores stores,
    IUnitOfWork unitOfWork) : ICommandHandler<AddClubTeamCommand, ClubDto>
{
    public async Task<ClubDto> Handle(AddClubTeamCommand command, CancellationToken cancellationToken)
    {
        var club = await Resolvers.CurrentClubAsync(currentUser, stores.Clubs, cancellationToken);

        var team = await stores.Teams.GetByIdAsync(command.TeamId, cancellationToken)
            ?? throw new NotFoundException(ErrorCode.TeamNotFound, command.TeamId);

        var entry = ClubCatalog.ForClub(await ClubCatalog.LoadAsync(stores, cancellationToken), club);
        if (entry is null || entry.Teams.All(t => t.Team.Id != team.Id))
            throw new DomainException(ErrorCode.ClubTeamNotFilial, team.Name, club.Name);

        if (await stores.Clubs.GetByTeamAsync(command.TeamId, cancellationToken) is { } owner && owner.Id != club.Id)
            throw new ConflictException(ErrorCode.ClubTeamLinkedElsewhere);

        club.AddTeam(command.TeamId, stores.Clock.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return await ClubMapper.ToDtoAsync(club, stores, cancellationToken, withFilials: true);
    }
}

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

        return await ClubMapper.ToDtoAsync(club, stores, cancellationToken, withFilials: true);
    }
}
