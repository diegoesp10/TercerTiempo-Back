using System.ComponentModel;
using FluentValidation;
using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Common;
using TercerTiempo.Domain.Clubs;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Common.Errors;
using TercerTiempo.Domain.Squads;

namespace TercerTiempo.Application.Squads.Injuries;

public sealed record ListInjuryTypesQuery : IQuery<IReadOnlyList<InjuryTypeDto>>;

public sealed record ListSquadInjuriesQuery(Guid SquadId) : IQuery<IReadOnlyList<InjuryDto>>;

public sealed record RecordInjuryCommand(
    Guid SquadId,
    Guid SquadPlayerId,
    InjuryType Type,
    [property: Description("Motivo o detalle, opcional (hasta 300 caracteres).")]
    string? Reason,
    DateOnly StartedOn,
    [property: Description("Fecha prevista de vuelta; si no se indica se propone con la duración media del tipo.")]
    DateOnly? ExpectedReturnOn) : ICommand<IReadOnlyList<InjuryDto>>;

public sealed record UpdateInjuryCommand(
    Guid SquadId,
    Guid InjuryId,
    InjuryType Type,
    string? Reason,
    DateOnly StartedOn,
    DateOnly? ExpectedReturnOn) : ICommand<IReadOnlyList<InjuryDto>>;

public sealed record CloseInjuryCommand(
    Guid SquadId,
    Guid InjuryId,
    [property: Description("Fecha del alta; por defecto hoy.")]
    DateOnly? ReturnedOn) : ICommand<IReadOnlyList<InjuryDto>>;

public sealed record ReopenInjuryCommand(Guid SquadId, Guid InjuryId) : ICommand<IReadOnlyList<InjuryDto>>;

public sealed record DeleteInjuryCommand(Guid SquadId, Guid InjuryId) : ICommand<IReadOnlyList<InjuryDto>>;

internal sealed class RecordInjuryValidator : AbstractValidator<RecordInjuryCommand>
{
    public RecordInjuryValidator()
    {
        RuleFor(x => x.SquadId).NotEmpty();
        RuleFor(x => x.SquadPlayerId).NotEmpty();
        RuleFor(x => x.Type).IsInEnum();
        RuleFor(x => x.Reason).MaximumLength(300);
        RuleFor(x => x.ExpectedReturnOn).GreaterThanOrEqualTo(x => x.StartedOn).When(x => x.ExpectedReturnOn.HasValue)
            .WithCatalogMessage(ErrorCode.InjuryDatesInvalid);
    }
}

internal sealed class UpdateInjuryValidator : AbstractValidator<UpdateInjuryCommand>
{
    public UpdateInjuryValidator()
    {
        RuleFor(x => x.SquadId).NotEmpty();
        RuleFor(x => x.InjuryId).NotEmpty();
        RuleFor(x => x.Type).IsInEnum();
        RuleFor(x => x.Reason).MaximumLength(300);
        RuleFor(x => x.ExpectedReturnOn).GreaterThanOrEqualTo(x => x.StartedOn).When(x => x.ExpectedReturnOn.HasValue)
            .WithCatalogMessage(ErrorCode.InjuryDatesInvalid);
    }
}

internal sealed class ListInjuryTypesHandler : IQueryHandler<ListInjuryTypesQuery, IReadOnlyList<InjuryTypeDto>>
{
    public Task<IReadOnlyList<InjuryTypeDto>> Handle(ListInjuryTypesQuery query, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<InjuryTypeDto>>(InjuryCatalog.Types.Select(InjuryMapper.ToDto).ToList());
}

// Las lesiones las ve el cuerpo técnico y los jugadores vinculados a la plantilla, nunca el público.
internal static class InjuryAccess
{
    public static async Task<Squad> SquadForViewerAsync(ISquadStore squads, IClubStore clubs, ICurrentUser currentUser, Guid squadId, TimeProvider clock, CancellationToken cancellationToken)
    {
        var squad = await squads.GetByIdAsync(squadId, cancellationToken)
            ?? throw new NotFoundException(ErrorCode.SquadNotFound, squadId);

        if (!currentUser.IsAuthenticated)
            throw new UnauthenticatedException();

        if (!await SquadAccess.IsInsiderAsync(squad, currentUser, clock, clubs, cancellationToken))
            throw new ForbiddenException(ErrorCode.SquadPrivate);

        return squad;
    }

    public static async Task<PlayerInjury> InjuryOfAsync(Squad squad, Guid injuryId, IInjuryStore injuries, CancellationToken cancellationToken)
    {
        var injury = await injuries.GetByIdAsync(injuryId, cancellationToken);
        if (injury is null || injury.SquadId != squad.Id)
            throw new NotFoundException(ErrorCode.InjuryNotFound, injuryId);
        return injury;
    }

    public static async Task<IReadOnlyList<InjuryDto>> ListAsync(Squad squad, IInjuryStore injuries, TimeProvider clock, CancellationToken cancellationToken) =>
        InjuryMapper.ToList(await injuries.GetBySquadAsync(squad.Id, cancellationToken), squad, SquadAccess.Today(clock));
}

internal sealed class ListSquadInjuriesHandler(ICurrentUser currentUser, ISquadStore squads, IClubStore clubs, IInjuryStore injuries, TimeProvider clock)
    : IQueryHandler<ListSquadInjuriesQuery, IReadOnlyList<InjuryDto>>
{
    public async Task<IReadOnlyList<InjuryDto>> Handle(ListSquadInjuriesQuery query, CancellationToken cancellationToken)
    {
        var squad = await InjuryAccess.SquadForViewerAsync(squads, clubs, currentUser, query.SquadId, clock, cancellationToken);
        return await InjuryAccess.ListAsync(squad, injuries, clock, cancellationToken);
    }
}

internal sealed class RecordInjuryHandler(ICurrentUser currentUser, ISquadStore squads, IInjuryStore injuries, IUnitOfWork unitOfWork, TimeProvider clock)
    : ICommandHandler<RecordInjuryCommand, IReadOnlyList<InjuryDto>>
{
    public async Task<IReadOnlyList<InjuryDto>> Handle(RecordInjuryCommand command, CancellationToken cancellationToken)
    {
        var squad = await Resolvers.SquadForStaffAsync(squads, currentUser, command.SquadId, clock, cancellationToken);
        var player = squad.ActivePlayers.FirstOrDefault(p => p.Id == command.SquadPlayerId)
            ?? throw new NotFoundException(ErrorCode.SquadPlayerNotFound, command.SquadPlayerId);

        var current = await injuries.GetBySquadAsync(squad.Id, cancellationToken);
        if (current.Any(i => i.IsActive && i.SquadPlayerId == player.Id))
            throw new ConflictException(ErrorCode.InjuryAlreadyActive, player.FullName);

        var expected = command.ExpectedReturnOn
            ?? (InjuryCatalog.Info(command.Type).AverageDays is { } days ? command.StartedOn.AddDays(days) : null);

        injuries.Add(PlayerInjury.Create(squad, player, currentUser.CoachId!.Value, command.Type, command.Reason, command.StartedOn, expected, clock.GetUtcNow()));
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return await InjuryAccess.ListAsync(squad, injuries, clock, cancellationToken);
    }
}

internal sealed class UpdateInjuryHandler(ICurrentUser currentUser, ISquadStore squads, IInjuryStore injuries, IUnitOfWork unitOfWork, TimeProvider clock)
    : ICommandHandler<UpdateInjuryCommand, IReadOnlyList<InjuryDto>>
{
    public async Task<IReadOnlyList<InjuryDto>> Handle(UpdateInjuryCommand command, CancellationToken cancellationToken)
    {
        var squad = await Resolvers.SquadForStaffAsync(squads, currentUser, command.SquadId, clock, cancellationToken);
        var injury = await InjuryAccess.InjuryOfAsync(squad, command.InjuryId, injuries, cancellationToken);

        injury.Update(command.Type, command.Reason, command.StartedOn, command.ExpectedReturnOn, clock.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return await InjuryAccess.ListAsync(squad, injuries, clock, cancellationToken);
    }
}

internal sealed class CloseInjuryHandler(ICurrentUser currentUser, ISquadStore squads, IInjuryStore injuries, IUnitOfWork unitOfWork, TimeProvider clock)
    : ICommandHandler<CloseInjuryCommand, IReadOnlyList<InjuryDto>>
{
    public async Task<IReadOnlyList<InjuryDto>> Handle(CloseInjuryCommand command, CancellationToken cancellationToken)
    {
        var squad = await Resolvers.SquadForStaffAsync(squads, currentUser, command.SquadId, clock, cancellationToken);
        var injury = await InjuryAccess.InjuryOfAsync(squad, command.InjuryId, injuries, cancellationToken);

        injury.Close(command.ReturnedOn ?? SquadAccess.Today(clock), clock.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return await InjuryAccess.ListAsync(squad, injuries, clock, cancellationToken);
    }
}

internal sealed class ReopenInjuryHandler(ICurrentUser currentUser, ISquadStore squads, IInjuryStore injuries, IUnitOfWork unitOfWork, TimeProvider clock)
    : ICommandHandler<ReopenInjuryCommand, IReadOnlyList<InjuryDto>>
{
    public async Task<IReadOnlyList<InjuryDto>> Handle(ReopenInjuryCommand command, CancellationToken cancellationToken)
    {
        var squad = await Resolvers.SquadForStaffAsync(squads, currentUser, command.SquadId, clock, cancellationToken);
        var injury = await InjuryAccess.InjuryOfAsync(squad, command.InjuryId, injuries, cancellationToken);

        var current = await injuries.GetBySquadAsync(squad.Id, cancellationToken);
        if (current.Any(i => i.IsActive && i.SquadPlayerId == injury.SquadPlayerId))
            throw new ConflictException(ErrorCode.InjuryAlreadyActive, squad.Players.FirstOrDefault(p => p.Id == injury.SquadPlayerId)?.FullName ?? "El jugador");

        injury.Reopen(clock.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return await InjuryAccess.ListAsync(squad, injuries, clock, cancellationToken);
    }
}

internal sealed class DeleteInjuryHandler(ICurrentUser currentUser, ISquadStore squads, IInjuryStore injuries, IUnitOfWork unitOfWork, TimeProvider clock)
    : ICommandHandler<DeleteInjuryCommand, IReadOnlyList<InjuryDto>>
{
    public async Task<IReadOnlyList<InjuryDto>> Handle(DeleteInjuryCommand command, CancellationToken cancellationToken)
    {
        var squad = await Resolvers.SquadForStaffAsync(squads, currentUser, command.SquadId, clock, cancellationToken);
        var injury = await InjuryAccess.InjuryOfAsync(squad, command.InjuryId, injuries, cancellationToken);

        injuries.Remove(injury);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return await InjuryAccess.ListAsync(squad, injuries, clock, cancellationToken);
    }
}
