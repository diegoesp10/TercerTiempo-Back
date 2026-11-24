using System.ComponentModel;
using FluentValidation;
using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Abstractions.Medical;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Common;
using TercerTiempo.Domain.Clubs;
using TercerTiempo.Domain.Coaches;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Common.Errors;
using TercerTiempo.Domain.Squads;

namespace TercerTiempo.Application.Squads.Injuries;

public sealed record ListInjuryTypesQuery : IQuery<IReadOnlyList<InjuryTypeDto>>;

public sealed record ListSquadInjuriesQuery(Guid SquadId) : IQuery<IReadOnlyList<InjuryDto>>;

public sealed record RecordInjuryCommand(
    Guid SquadId,
    Guid SquadPlayerId,
    [property: Description("Código del tipo (GET /api/squads/injury-types).")]
    string Type,
    [property: Description("Motivo o detalle, opcional (hasta 300 caracteres).")]
    string? Reason,
    DateOnly StartedOn,
    [property: Description("Fecha prevista de vuelta; si no se indica se propone con la duración media del tipo.")]
    DateOnly? ExpectedReturnOn) : ICommand<IReadOnlyList<InjuryDto>>;

public sealed record UpdateInjuryCommand(
    Guid SquadId,
    Guid InjuryId,
    string Type,
    string? Reason,
    DateOnly StartedOn,
    DateOnly? ExpectedReturnOn) : ICommand<IReadOnlyList<InjuryDto>>;

public sealed record CloseInjuryCommand(
    Guid SquadId,
    Guid InjuryId,
    [property: Description("Fecha del alta; por defecto hoy.")]
    DateOnly? ReturnedOn) : ICommand<IReadOnlyList<InjuryDto>>;

public sealed record ReopenInjuryCommand(Guid SquadId, Guid InjuryId) : ICommand<IReadOnlyList<InjuryDto>>;

// La lesión superada vuelve: se registra como una nueva del mismo tipo, enlazada a aquella, que conserva su alta y sus días.
public sealed record RecordRelapseCommand(
    Guid SquadId,
    Guid InjuryId,
    [property: Description("Fecha del parte de la recaída: no antes del alta de la lesión anterior ni después de hoy.")]
    DateOnly StartedOn,
    [property: Description("Fecha prevista de vuelta; si no se indica se propone con la duración media del tipo.")]
    DateOnly? ExpectedReturnOn,
    [property: Description("Motivo o detalle, opcional (hasta 300 caracteres).")]
    string? Reason) : ICommand<IReadOnlyList<InjuryDto>>;

public sealed record DeleteInjuryCommand(Guid SquadId, Guid InjuryId) : ICommand<IReadOnlyList<InjuryDto>>;

internal sealed class RecordInjuryValidator : AbstractValidator<RecordInjuryCommand>
{
    public RecordInjuryValidator()
    {
        RuleFor(x => x.SquadId).NotEmpty();
        RuleFor(x => x.SquadPlayerId).NotEmpty();
        RuleFor(x => x.Type).NotEmpty().MaximumLength(InjuryTypeDefinition.CodeMaxLength).WithCatalogMessage(ErrorCode.InjuryInvalidType);
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
        RuleFor(x => x.Type).NotEmpty().MaximumLength(InjuryTypeDefinition.CodeMaxLength).WithCatalogMessage(ErrorCode.InjuryInvalidType);
        RuleFor(x => x.Reason).MaximumLength(300);
        RuleFor(x => x.ExpectedReturnOn).GreaterThanOrEqualTo(x => x.StartedOn).When(x => x.ExpectedReturnOn.HasValue)
            .WithCatalogMessage(ErrorCode.InjuryDatesInvalid);
    }
}

internal sealed class RecordRelapseValidator : AbstractValidator<RecordRelapseCommand>
{
    public RecordRelapseValidator()
    {
        RuleFor(x => x.SquadId).NotEmpty();
        RuleFor(x => x.InjuryId).NotEmpty();
        RuleFor(x => x.Reason).MaximumLength(300);
        RuleFor(x => x.ExpectedReturnOn).GreaterThanOrEqualTo(x => x.StartedOn).When(x => x.ExpectedReturnOn.HasValue)
            .WithCatalogMessage(ErrorCode.InjuryDatesInvalid);
    }
}

// Solo los que se pueden elegir: los retirados siguen en las lesiones que ya los tienen, pero no se ofrecen.
internal sealed class ListInjuryTypesHandler(IInjuryTypeStore types) : IQueryHandler<ListInjuryTypesQuery, IReadOnlyList<InjuryTypeDto>>
{
    public async Task<IReadOnlyList<InjuryTypeDto>> Handle(ListInjuryTypesQuery query, CancellationToken cancellationToken) =>
        InjuryTypeCatalog.Sorted(await types.GetAllAsync(cancellationToken)).Where(t => t.Active).Select(InjuryMapper.ToDto).ToList();
}

internal static class InjuryTypeCatalog
{
    public static IEnumerable<InjuryTypeDefinition> Sorted(IEnumerable<InjuryTypeDefinition> types) =>
        types.OrderBy(t => t.SortOrder).ThenBy(t => t.Label, StringComparer.CurrentCultureIgnoreCase);

    public static async Task<InjuryTypeDefinition> ForInjuryAsync(IInjuryTypeStore types, string code, CancellationToken cancellationToken) =>
        await types.GetByCodeAsync(code.Trim(), cancellationToken) ?? throw new DomainException(ErrorCode.InjuryInvalidType);
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

    // El parte y el alta son de días ya vividos: el contador de días de baja tiene que ser real. Lo que aún no ha pasado va
    // en la vuelta prevista.
    public static void EnsureNotFuture(DateOnly day, string errorCode, TimeProvider clock)
    {
        if (day > SquadAccess.Today(clock))
            throw new DomainException(errorCode);
    }

    // Las lesiones de quien causó baja son historial: se consultan y se pueden borrar (son datos de salud), pero no se
    // corrigen, ni se les da el alta o una recaída, ni se les suben pruebas.
    public static void EnsurePlayerInSquad(Squad squad, PlayerInjury injury)
    {
        if (squad.Players.FirstOrDefault(p => p.Id == injury.SquadPlayerId) is { IsActive: false } player)
            throw new DomainException(ErrorCode.SquadPlayerLeftReadOnly, player.FullName);
    }

    public static async Task<IReadOnlyList<InjuryDto>> ListAsync(
        Squad squad, IInjuryStore injuries, IInjuryDocumentStore documents, TimeProvider clock, CancellationToken cancellationToken)
    {
        var list = await injuries.GetBySquadAsync(squad.Id, cancellationToken);
        var attached = list.Count == 0 ? [] : await documents.GetByInjuryIdsAsync(list.Select(i => i.Id).ToList(), cancellationToken);
        return InjuryMapper.ToList(list, squad, SquadAccess.Today(clock), attached.ToLookup(d => d.InjuryId));
    }

    // Las pruebas e informes médicos los abren el club, el cuerpo técnico y el propio jugador. El administrador no: puede
    // revisar la plataforma en solo lectura, pero una radiografía no le hace falta para eso.
    public static async Task<bool> CanOpenDocumentsAsync(
        Squad squad, PlayerInjury injury, ICurrentUser currentUser, TimeProvider clock, IClubStore clubs, CancellationToken cancellationToken) =>
        await SquadAccess.IsManagerAsync(squad, currentUser, clock, clubs, cancellationToken)
        || (currentUser.PlayerId is { } playerId && squad.Players.Any(p => p.Id == injury.SquadPlayerId && p.PlayerId == playerId));

    // Son datos de salud, muchas veces de menores: el club, el cuerpo técnico y el administrador las ven todas; un jugador,
    // solo las suyas, nunca las de sus compañeros.
    public static async Task<Func<Guid, bool>> VisibleSquadPlayersAsync(Squad squad, ICurrentUser currentUser, TimeProvider clock, IClubStore clubs, CancellationToken cancellationToken)
    {
        if (currentUser.IsAdmin || await SquadAccess.IsManagerAsync(squad, currentUser, clock, clubs, cancellationToken))
            return _ => true;

        var own = squad.Players.Where(p => currentUser.PlayerId is { } playerId && p.PlayerId == playerId).Select(p => p.Id).ToHashSet();
        return own.Contains;
    }
}

internal sealed class ListSquadInjuriesHandler(ICurrentUser currentUser, ISquadStore squads, IClubStore clubs, IInjuryStore injuries, IInjuryDocumentStore documents, TimeProvider clock)
    : IQueryHandler<ListSquadInjuriesQuery, IReadOnlyList<InjuryDto>>
{
    public async Task<IReadOnlyList<InjuryDto>> Handle(ListSquadInjuriesQuery query, CancellationToken cancellationToken)
    {
        var squad = await InjuryAccess.SquadForViewerAsync(squads, clubs, currentUser, query.SquadId, clock, cancellationToken);
        var visible = await InjuryAccess.VisibleSquadPlayersAsync(squad, currentUser, clock, clubs, cancellationToken);
        return (await InjuryAccess.ListAsync(squad, injuries, documents, clock, cancellationToken)).Where(i => visible(i.SquadPlayerId)).ToList();
    }
}

internal sealed class RecordInjuryHandler(
    ICurrentUser currentUser, ISquadStore squads, ICoachStore coaches, IClubStore clubs, IInjuryStore injuries, IInjuryTypeStore types,
    IInjuryDocumentStore documents, IUnitOfWork unitOfWork, TimeProvider clock)
    : ICommandHandler<RecordInjuryCommand, IReadOnlyList<InjuryDto>>
{
    public async Task<IReadOnlyList<InjuryDto>> Handle(RecordInjuryCommand command, CancellationToken cancellationToken)
    {
        var squad = await Resolvers.SquadForManagerAsync(squads, currentUser, clubs, command.SquadId, clock, cancellationToken);
        var player = squad.ActivePlayers.FirstOrDefault(p => p.Id == command.SquadPlayerId)
            ?? throw new NotFoundException(ErrorCode.SquadPlayerNotFound, command.SquadPlayerId);

        InjuryAccess.EnsureNotFuture(command.StartedOn, ErrorCode.InjuryStartInFuture, clock);
        var current = await injuries.GetBySquadAsync(squad.Id, cancellationToken);
        if (current.Any(i => i.IsActive && i.SquadPlayerId == player.Id))
            throw new ConflictException(ErrorCode.InjuryAlreadyActive, player.FullName);

        var type = await InjuryTypeCatalog.ForInjuryAsync(types, command.Type, cancellationToken);
        var expected = command.ExpectedReturnOn ?? (type.AverageDays is { } days ? command.StartedOn.AddDays(days) : null);

        var editor = await Resolvers.CurrentEditorAsync(currentUser, coaches, clubs, cancellationToken);
        injuries.Add(PlayerInjury.Create(squad, player, editor, type, command.Reason, command.StartedOn, expected, clock.GetUtcNow()));
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return await InjuryAccess.ListAsync(squad, injuries, documents, clock, cancellationToken);
    }
}

internal sealed class UpdateInjuryHandler(
    ICurrentUser currentUser, ISquadStore squads, IClubStore clubs, IInjuryStore injuries, IInjuryTypeStore types,
    IInjuryDocumentStore documents, IUnitOfWork unitOfWork, TimeProvider clock)
    : ICommandHandler<UpdateInjuryCommand, IReadOnlyList<InjuryDto>>
{
    public async Task<IReadOnlyList<InjuryDto>> Handle(UpdateInjuryCommand command, CancellationToken cancellationToken)
    {
        var squad = await Resolvers.SquadForManagerAsync(squads, currentUser, clubs, command.SquadId, clock, cancellationToken);
        var injury = await InjuryAccess.InjuryOfAsync(squad, command.InjuryId, injuries, cancellationToken);
        InjuryAccess.EnsurePlayerInSquad(squad, injury);
        injury.EnsureNotContinued();
        InjuryAccess.EnsureNotFuture(command.StartedOn, ErrorCode.InjuryStartInFuture, clock);

        // Una recaída no puede empezar antes del alta de la lesión de la que viene.
        if (injury.RelapseOfInjuryId is { } previousId
            && await injuries.GetByIdAsync(previousId, cancellationToken) is { ReturnedOn: { } previousReturn }
            && command.StartedOn < previousReturn)
            throw new DomainException(ErrorCode.InjuryRelapseBeforeReturn);

        var type = await InjuryTypeCatalog.ForInjuryAsync(types, command.Type, cancellationToken);
        injury.Update(type, command.Reason, command.StartedOn, command.ExpectedReturnOn, clock.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return await InjuryAccess.ListAsync(squad, injuries, documents, clock, cancellationToken);
    }
}

internal sealed class CloseInjuryHandler(ICurrentUser currentUser, ISquadStore squads, IClubStore clubs, IInjuryStore injuries, IInjuryDocumentStore documents, IUnitOfWork unitOfWork, TimeProvider clock)
    : ICommandHandler<CloseInjuryCommand, IReadOnlyList<InjuryDto>>
{
    public async Task<IReadOnlyList<InjuryDto>> Handle(CloseInjuryCommand command, CancellationToken cancellationToken)
    {
        var squad = await Resolvers.SquadForManagerAsync(squads, currentUser, clubs, command.SquadId, clock, cancellationToken);
        var injury = await InjuryAccess.InjuryOfAsync(squad, command.InjuryId, injuries, cancellationToken);
        InjuryAccess.EnsurePlayerInSquad(squad, injury);
        injury.EnsureNotContinued();

        var returnedOn = command.ReturnedOn ?? SquadAccess.Today(clock);
        InjuryAccess.EnsureNotFuture(returnedOn, ErrorCode.InjuryReturnInFuture, clock);
        injury.Close(returnedOn, clock.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return await InjuryAccess.ListAsync(squad, injuries, documents, clock, cancellationToken);
    }
}

internal sealed class ReopenInjuryHandler(ICurrentUser currentUser, ISquadStore squads, IClubStore clubs, IInjuryStore injuries, IInjuryDocumentStore documents, IUnitOfWork unitOfWork, TimeProvider clock)
    : ICommandHandler<ReopenInjuryCommand, IReadOnlyList<InjuryDto>>
{
    public async Task<IReadOnlyList<InjuryDto>> Handle(ReopenInjuryCommand command, CancellationToken cancellationToken)
    {
        var squad = await Resolvers.SquadForManagerAsync(squads, currentUser, clubs, command.SquadId, clock, cancellationToken);
        var injury = await InjuryAccess.InjuryOfAsync(squad, command.InjuryId, injuries, cancellationToken);
        InjuryAccess.EnsurePlayerInSquad(squad, injury);
        injury.EnsureNotContinued();

        var current = await injuries.GetBySquadAsync(squad.Id, cancellationToken);
        if (current.Any(i => i.IsActive && i.SquadPlayerId == injury.SquadPlayerId))
            throw new ConflictException(ErrorCode.InjuryAlreadyActive, squad.Players.FirstOrDefault(p => p.Id == injury.SquadPlayerId)?.FullName ?? "El jugador");
        // Con una recaída detrás, el alta fue real: deshacerla dejaría dos lesiones solapadas.
        if (current.Any(i => i.RelapseOfInjuryId == injury.Id))
            throw new ConflictException(ErrorCode.InjuryHasRelapse);

        injury.Reopen(clock.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return await InjuryAccess.ListAsync(squad, injuries, documents, clock, cancellationToken);
    }
}

internal sealed class RecordRelapseHandler(
    ICurrentUser currentUser, ISquadStore squads, ICoachStore coaches, IClubStore clubs, IInjuryStore injuries, IInjuryDocumentStore documents,
    IUnitOfWork unitOfWork, TimeProvider clock)
    : ICommandHandler<RecordRelapseCommand, IReadOnlyList<InjuryDto>>
{
    public async Task<IReadOnlyList<InjuryDto>> Handle(RecordRelapseCommand command, CancellationToken cancellationToken)
    {
        var squad = await Resolvers.SquadForManagerAsync(squads, currentUser, clubs, command.SquadId, clock, cancellationToken);
        var previous = await InjuryAccess.InjuryOfAsync(squad, command.InjuryId, injuries, cancellationToken);
        InjuryAccess.EnsurePlayerInSquad(squad, previous);
        previous.EnsureNotContinued();
        InjuryAccess.EnsureNotFuture(command.StartedOn, ErrorCode.InjuryStartInFuture, clock);

        var player = squad.ActivePlayers.FirstOrDefault(p => p.Id == previous.SquadPlayerId)
            ?? throw new NotFoundException(ErrorCode.SquadPlayerNotFound, previous.SquadPlayerId);
        var current = await injuries.GetBySquadAsync(squad.Id, cancellationToken);
        if (current.Any(i => i.IsActive && i.SquadPlayerId == player.Id))
            throw new ConflictException(ErrorCode.InjuryAlreadyActive, player.FullName);

        var type = previous.TypeDefinition;
        var expected = command.ExpectedReturnOn ?? (type.AverageDays is { } days ? command.StartedOn.AddDays(days) : null);
        var editor = await Resolvers.CurrentEditorAsync(currentUser, coaches, clubs, cancellationToken);
        injuries.Add(PlayerInjury.Create(squad, player, editor, type, command.Reason, command.StartedOn, expected, clock.GetUtcNow(), relapseOf: previous));
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return await InjuryAccess.ListAsync(squad, injuries, documents, clock, cancellationToken);
    }
}

// Con la lesión se van sus pruebas e informes: primero de la base de datos y, ya guardado, los ficheros. Si pasó abierta a
// la temporada siguiente, la de allí se queda (con su parte), ya sin enlace; si es la continuación, la de la temporada
// anterior sigue como historial y no se vuelve a pasar.
internal sealed class DeleteInjuryHandler(
    ICurrentUser currentUser, ISquadStore squads, IClubStore clubs, IInjuryStore injuries, IInjuryDocumentStore documents,
    IInjuryDocumentStorage storage, IUnitOfWork unitOfWork, TimeProvider clock)
    : ICommandHandler<DeleteInjuryCommand, IReadOnlyList<InjuryDto>>
{
    public async Task<IReadOnlyList<InjuryDto>> Handle(DeleteInjuryCommand command, CancellationToken cancellationToken)
    {
        var squad = await Resolvers.SquadForManagerAsync(squads, currentUser, clubs, command.SquadId, clock, cancellationToken);
        var injury = await InjuryAccess.InjuryOfAsync(squad, command.InjuryId, injuries, cancellationToken);

        var attached = await documents.GetByInjuryIdsAsync([injury.Id], cancellationToken);
        foreach (var document in attached)
            documents.Remove(document);
        // Sus recaídas se quedan, ya sin enlace: lo que se borra es solo lo que se registró por error.
        var now = clock.GetUtcNow();
        foreach (var relapse in (await injuries.GetBySquadAsync(squad.Id, cancellationToken)).Where(i => i.RelapseOfInjuryId == injury.Id))
            relapse.ForgetRelapseOf(now);
        if (injury.ContinuedAt is not null && await injuries.GetContinuationAsync(injury.Id, cancellationToken) is { } continuation)
            continuation.ForgetContinues(now);
        injuries.Remove(injury);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        foreach (var document in attached)
            await storage.DeleteAsync(document.StorageKey, CancellationToken.None);

        return await InjuryAccess.ListAsync(squad, injuries, documents, clock, cancellationToken);
    }
}
