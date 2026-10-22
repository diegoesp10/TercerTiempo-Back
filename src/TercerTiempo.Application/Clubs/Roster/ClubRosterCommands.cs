using System.ComponentModel;
using FluentValidation;
using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Auth;
using TercerTiempo.Application.Common;
using TercerTiempo.Application.Teams;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Players;
using TercerTiempo.Domain.Teams;
using TercerTiempo.Domain.Common.Errors;

namespace TercerTiempo.Application.Clubs.Roster;

public sealed record AddClubRosterEntryCommand(
    Guid TeamId,
    [property: Description("Nombre y apellidos.")]
    string FullName,
    Position? Position,
    [property: Description("1-99, opcional.")]
    int? ShirtNumber) : ICommand<IReadOnlyList<RosterEntryDto>>;

public sealed record UpdateClubRosterEntryCommand(
    Guid TeamId,
    Guid EntryId,
    string FullName,
    Position? Position,
    int? ShirtNumber) : ICommand<IReadOnlyList<RosterEntryDto>>;

public sealed record RemoveClubRosterEntryCommand(Guid TeamId, Guid EntryId) : ICommand<IReadOnlyList<RosterEntryDto>>;

internal sealed class AddClubRosterEntryValidator : AbstractValidator<AddClubRosterEntryCommand>
{
    public AddClubRosterEntryValidator()
    {
        RuleFor(x => x.TeamId).NotEmpty();
        RuleFor(x => x.FullName).NotEmpty().MinimumLength(2).MaximumLength(150);
        RuleFor(x => x.Position).IsInEnum().When(x => x.Position.HasValue);
        RuleFor(x => x.ShirtNumber).InclusiveBetween(1, 99).When(x => x.ShirtNumber.HasValue);
    }
}

internal sealed class UpdateClubRosterEntryValidator : AbstractValidator<UpdateClubRosterEntryCommand>
{
    public UpdateClubRosterEntryValidator()
    {
        RuleFor(x => x.TeamId).NotEmpty();
        RuleFor(x => x.EntryId).NotEmpty();
        RuleFor(x => x.FullName).NotEmpty().MinimumLength(2).MaximumLength(150);
        RuleFor(x => x.Position).IsInEnum().When(x => x.Position.HasValue);
        RuleFor(x => x.ShirtNumber).InclusiveBetween(1, 99).When(x => x.ShirtNumber.HasValue);
    }
}

internal sealed class RemoveClubRosterEntryValidator : AbstractValidator<RemoveClubRosterEntryCommand>
{
    public RemoveClubRosterEntryValidator()
    {
        RuleFor(x => x.TeamId).NotEmpty();
        RuleFor(x => x.EntryId).NotEmpty();
    }
}

internal static class ClubRoster
{
    public static async Task<(Guid SeasonId, DateTimeOffset Now)> ContextAsync(Guid teamId, ICurrentUser currentUser, AccountStores stores, CancellationToken cancellationToken)
    {
        var club = await Resolvers.CurrentClubAsync(currentUser, stores.Clubs, cancellationToken);
        if (!club.Manages(teamId))
            throw new ForbiddenException(ErrorCode.ClubRosterOwnTeamsOnly);

        var (_, season) = await Resolvers.SeasonOfTeamAsync(stores.Competitions, teamId, cancellationToken);
        return (season.Id, stores.Clock.GetUtcNow());
    }

    public static async Task<IReadOnlyList<RosterEntryDto>> ListAsync(Guid teamId, Guid seasonId, ITeamStore teams, CancellationToken cancellationToken) =>
        (await teams.GetRosterAsync(teamId, seasonId, cancellationToken))
            .OrderBy(r => r.ShirtNumber ?? int.MaxValue)
            .ThenBy(r => r.FullName)
            .Select(RosterEntryDto.From)
            .ToList();

    public static async Task<RosterEntry> EntryAsync(Guid teamId, Guid seasonId, Guid entryId, ITeamStore teams, CancellationToken cancellationToken)
    {
        var entry = await teams.GetRosterEntryAsync(entryId, cancellationToken);
        if (entry is null || entry.TeamId != teamId || entry.SeasonId != seasonId)
            throw new NotFoundException(ErrorCode.RosterEntryNotFound, entryId);
        return entry;
    }
}

internal sealed class AddClubRosterEntryHandler(ICurrentUser currentUser, AccountStores stores, IUnitOfWork unitOfWork)
    : ICommandHandler<AddClubRosterEntryCommand, IReadOnlyList<RosterEntryDto>>
{
    public async Task<IReadOnlyList<RosterEntryDto>> Handle(AddClubRosterEntryCommand command, CancellationToken cancellationToken)
    {
        var (seasonId, now) = await ClubRoster.ContextAsync(command.TeamId, currentUser, stores, cancellationToken);

        var roster = await stores.Teams.GetRosterAsync(command.TeamId, seasonId, cancellationToken);
        if (roster.Any(r => r.Matches(command.FullName)))
            throw new ConflictException(ErrorCode.RosterEntryDuplicateName);

        stores.Teams.AddRosterEntry(RosterEntry.CreateByClub(command.TeamId, seasonId, command.FullName.Trim(), command.Position, command.ShirtNumber, now));
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return await ClubRoster.ListAsync(command.TeamId, seasonId, stores.Teams, cancellationToken);
    }
}

internal sealed class UpdateClubRosterEntryHandler(ICurrentUser currentUser, AccountStores stores, IUnitOfWork unitOfWork)
    : ICommandHandler<UpdateClubRosterEntryCommand, IReadOnlyList<RosterEntryDto>>
{
    public async Task<IReadOnlyList<RosterEntryDto>> Handle(UpdateClubRosterEntryCommand command, CancellationToken cancellationToken)
    {
        var (seasonId, now) = await ClubRoster.ContextAsync(command.TeamId, currentUser, stores, cancellationToken);
        var entry = await ClubRoster.EntryAsync(command.TeamId, seasonId, command.EntryId, stores.Teams, cancellationToken);

        entry.UpdateByClub(command.FullName.Trim(), command.Position, command.ShirtNumber, now);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return await ClubRoster.ListAsync(command.TeamId, seasonId, stores.Teams, cancellationToken);
    }
}

internal sealed class RemoveClubRosterEntryHandler(ICurrentUser currentUser, AccountStores stores, IUnitOfWork unitOfWork)
    : ICommandHandler<RemoveClubRosterEntryCommand, IReadOnlyList<RosterEntryDto>>
{
    public async Task<IReadOnlyList<RosterEntryDto>> Handle(RemoveClubRosterEntryCommand command, CancellationToken cancellationToken)
    {
        var (seasonId, _) = await ClubRoster.ContextAsync(command.TeamId, currentUser, stores, cancellationToken);
        var entry = await ClubRoster.EntryAsync(command.TeamId, seasonId, command.EntryId, stores.Teams, cancellationToken);

        // Las fichas del proveedor se ocultan (volverían al sincronizar); las del club se borran.
        if (entry.ExternalRef is null)
            stores.Teams.RemoveRosterEntry(entry);
        else
            entry.Hide();

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return await ClubRoster.ListAsync(command.TeamId, seasonId, stores.Teams, cancellationToken);
    }
}
