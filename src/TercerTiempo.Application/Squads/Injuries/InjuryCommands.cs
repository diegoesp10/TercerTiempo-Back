using System.ComponentModel;
using FluentValidation;
using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Abstractions.Medical;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Common;
using TercerTiempo.Application.Trainings;
using TercerTiempo.Domain.Alerts;
using TercerTiempo.Domain.Clubs;
using TercerTiempo.Domain.Coaches;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Common.Errors;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Teams;
using TercerTiempo.Domain.Trainings;

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
    DateOnly? ExpectedReturnOn,
    [property: Description("Posible lesión de una crónica que se registra con ella: queda registrada y su aviso, resuelto. Se lesionó en su partido.")]
    Guid? ProposalId = null,
    [property: Description("Partido de liga en que se lesionó (GET /api/squads/{squadId}/injury-occasions).")]
    Guid? MatchId = null,
    [property: Description("Entrenamiento de la plantilla en que se lesionó. Sin partido ni entrenamiento, fue en otro sitio o no se sabe.")]
    Guid? TrainingSessionId = null,
    [property: Description("Sitio del cuerpo (GET /api/squads/body-sites), el que pregunte el tipo; con sitio fijo, si no se precisa, ese.")]
    string? Site = null,
    [property: Description("Lado, si el sitio tiene dos: Left, Right o Both (obligatorio entonces).")]
    BodySide? Side = null,
    [property: Description("Grado, si el tipo lo lleva y se sabe: I, II o III.")]
    InjuryGrade? Grade = null,
    [property: Description("Cómo se produjo, si se sabe: Contact, NonContact u Overuse.")]
    InjuryMechanism? Mechanism = null) : ICommand<IReadOnlyList<InjuryDto>>;

public sealed record UpdateInjuryCommand(
    Guid SquadId,
    Guid InjuryId,
    string Type,
    string? Reason,
    DateOnly StartedOn,
    DateOnly? ExpectedReturnOn,
    [property: Description("Partido de liga en que se lesionó (GET /api/squads/{squadId}/injury-occasions).")]
    Guid? MatchId = null,
    [property: Description("Entrenamiento de la plantilla en que se lesionó. Sin partido ni entrenamiento, fue en otro sitio o no se sabe.")]
    Guid? TrainingSessionId = null,
    [property: Description("Sitio del cuerpo (GET /api/squads/body-sites), el que pregunte el tipo; con sitio fijo, si no se precisa, ese.")]
    string? Site = null,
    [property: Description("Lado, si el sitio tiene dos: Left, Right o Both (obligatorio entonces).")]
    BodySide? Side = null,
    [property: Description("Grado, si el tipo lo lleva y se sabe: I, II o III.")]
    InjuryGrade? Grade = null,
    [property: Description("Cómo se produjo, si se sabe: Contact, NonContact u Overuse.")]
    InjuryMechanism? Mechanism = null) : ICommand<IReadOnlyList<InjuryDto>>;

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
    string? Reason,
    [property: Description("Partido de liga en que se lesionó (GET /api/squads/{squadId}/injury-occasions).")]
    Guid? MatchId = null,
    [property: Description("Entrenamiento de la plantilla en que se lesionó. Sin partido ni entrenamiento, fue en otro sitio o no se sabe.")]
    Guid? TrainingSessionId = null,
    [property: Description("Sitio del cuerpo; si no se indica, el de la lesión anterior (es la misma lesión que vuelve).")]
    string? Site = null,
    [property: Description("Lado; si no se indica, el de la lesión anterior.")]
    BodySide? Side = null,
    [property: Description("Grado de la recaída, si el tipo lo lleva y se sabe: I, II o III.")]
    InjuryGrade? Grade = null,
    [property: Description("Cómo se produjo la recaída, si se sabe: Contact, NonContact u Overuse.")]
    InjuryMechanism? Mechanism = null) : ICommand<IReadOnlyList<InjuryDto>>;

public sealed record ListBodySitesQuery : IQuery<IReadOnlyList<BodySiteDto>>;

public sealed record DeleteInjuryCommand(Guid SquadId, Guid InjuryId) : ICommand<IReadOnlyList<InjuryDto>>;

internal sealed class RecordInjuryValidator : AbstractValidator<RecordInjuryCommand>
{
    public RecordInjuryValidator()
    {
        RuleFor(x => x.SquadId).NotEmpty();
        RuleFor(x => x.SquadPlayerId).NotEmpty();
        RuleFor(x => x.Type).NotEmpty().MaximumLength(InjuryTypeDefinition.CodeMaxLength).WithCatalogMessage(ErrorCode.InjuryInvalidType);
        RuleFor(x => x.Site).MaximumLength(BodySite.CodeMaxLength).WithName("Sitio del cuerpo");
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
        RuleFor(x => x.Site).MaximumLength(BodySite.CodeMaxLength).WithName("Sitio del cuerpo");
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
        RuleFor(x => x.Site).MaximumLength(BodySite.CodeMaxLength).WithName("Sitio del cuerpo");
        RuleFor(x => x.Reason).MaximumLength(300);
        RuleFor(x => x.ExpectedReturnOn).GreaterThanOrEqualTo(x => x.StartedOn).When(x => x.ExpectedReturnOn.HasValue)
            .WithCatalogMessage(ErrorCode.InjuryDatesInvalid);
    }
}

// Todos, también los retirados (marcados): no se ofrecen para lesiones nuevas, pero la lesión que ya tiene uno se corrige
// con lo que pregunta (su sitio y su lado).
internal sealed class ListInjuryTypesHandler(IInjuryTypeStore types) : IQueryHandler<ListInjuryTypesQuery, IReadOnlyList<InjuryTypeDto>>
{
    public async Task<IReadOnlyList<InjuryTypeDto>> Handle(ListInjuryTypesQuery query, CancellationToken cancellationToken) =>
        InjuryTypeCatalog.Sorted(await types.GetAllAsync(cancellationToken)).Select(InjuryMapper.ToDto).ToList();
}

internal static class InjuryTypeCatalog
{
    public static IEnumerable<InjuryTypeDefinition> Sorted(IEnumerable<InjuryTypeDefinition> types) =>
        types.OrderBy(t => t.SortOrder).ThenBy(t => t.Label, StringComparer.CurrentCultureIgnoreCase);

    public static async Task<InjuryTypeDefinition> ForInjuryAsync(IInjuryTypeStore types, string code, CancellationToken cancellationToken) =>
        await types.GetByCodeAsync(code.Trim(), cancellationToken) ?? throw new DomainException(ErrorCode.InjuryInvalidType);
}

// Todos, también los retirados (marcados): una lesión que ya tiene uno lo sigue enseñando y se corrige con él.
internal sealed class ListBodySitesHandler(IBodySiteStore sites) : IQueryHandler<ListBodySitesQuery, IReadOnlyList<BodySiteDto>>
{
    public async Task<IReadOnlyList<BodySiteDto>> Handle(ListBodySitesQuery query, CancellationToken cancellationToken) =>
        BodySiteCatalog.Sorted(await sites.GetAllAsync(cancellationToken)).Select(InjuryMapper.ToDto).ToList();
}

internal static class BodySiteCatalog
{
    // De la cabeza a los pies y, en cada zona, en su orden.
    public static IEnumerable<BodySite> Sorted(IEnumerable<BodySite> sites) =>
        sites.OrderBy(s => s.Region).ThenBy(s => s.SortOrder).ThenBy(s => s.Label, StringComparer.CurrentCultureIgnoreCase);

    public static async Task<InjuryDetail> DetailAsync(
        IBodySiteStore sites, string? site, BodySide? side, InjuryGrade? grade, InjuryMechanism? mechanism, CancellationToken cancellationToken)
    {
        BodySite? found = null;
        if (!string.IsNullOrWhiteSpace(site))
            found = await sites.GetByCodeAsync(site.Trim(), cancellationToken) ?? throw new DomainException(ErrorCode.InjurySiteNotFound, site.Trim());
        return new InjuryDetail(found, side, grade, mechanism);
    }
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
    IInjuryDocumentStore documents, IInjuryProposalStore proposals, IAlertStore alerts, IMatchStore matches, ITeamStore teams, ITrainingStore trainings,
    IUnitOfWork unitOfWork, TimeProvider clock, IBodySiteStore sites)
    : ICommandHandler<RecordInjuryCommand, IReadOnlyList<InjuryDto>>
{
    public async Task<IReadOnlyList<InjuryDto>> Handle(RecordInjuryCommand command, CancellationToken cancellationToken)
    {
        var squad = await Resolvers.SquadForManagerAsync(squads, currentUser, clubs, command.SquadId, clock, cancellationToken);
        var player = squad.ActivePlayers.FirstOrDefault(p => p.Id == command.SquadPlayerId)
            ?? throw new NotFoundException(ErrorCode.SquadPlayerNotFound, command.SquadPlayerId);
        var proposal = command.ProposalId is { } proposalId
            ? await InjuryProposalAccess.PendingOfAsync(squad, proposalId, proposals, cancellationToken)
            : null;
        if (proposal is not null && proposal.SquadPlayerId != player.Id)
            throw new DomainException(ErrorCode.InjuryProposalMismatch);
        // La de la crónica de un partido se hizo en ese partido.
        var matchId = command.MatchId;
        if (proposal is not null)
        {
            if ((command.TrainingSessionId is { } session && session != Guid.Empty) || (matchId is { } match && match != Guid.Empty && match != proposal.MatchId))
                throw new DomainException(ErrorCode.InjuryProposalOccasion);
            matchId = proposal.MatchId;
        }

        InjuryAccess.EnsureNotFuture(command.StartedOn, ErrorCode.InjuryStartInFuture, clock);
        var current = await injuries.GetBySquadAsync(squad.Id, cancellationToken);
        if (current.Any(i => i.IsActive && i.SquadPlayerId == player.Id))
            throw new ConflictException(ErrorCode.InjuryAlreadyActive, player.FullName);

        var type = await InjuryTypeCatalog.ForInjuryAsync(types, command.Type, cancellationToken);
        var expected = command.ExpectedReturnOn ?? (type.AverageDays is { } days ? command.StartedOn.AddDays(days) : null);
        var detail = await BodySiteCatalog.DetailAsync(sites, command.Site, command.Side, command.Grade, command.Mechanism, cancellationToken);

        var editor = await Resolvers.CurrentEditorAsync(currentUser, coaches, clubs, cancellationToken);
        var now = clock.GetUtcNow();
        var occasion = await InjuryOccasions.ResolveAsync(squad, matchId, command.TrainingSessionId, null, matches, teams, trainings, now, cancellationToken);
        var injury = PlayerInjury.Create(
            squad, player, editor, type, command.Reason, command.StartedOn, expected, now, occasion: occasion, detail: detail);
        injuries.Add(injury);
        if (proposal is not null)
        {
            proposal.Register(injury.Id, editor, now);
            await StaffAlerts.ResolveAsync(squad, clubs, alerts, InjuryProposalAccess.KeyFor(proposal.Id), clock, cancellationToken);
        }
        await InjuryOccasions.SyncProposalsAsync(squad, injury, editor, proposals, clubs, alerts, clock, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return await InjuryAccess.ListAsync(squad, injuries, documents, clock, cancellationToken);
    }
}

internal sealed class UpdateInjuryHandler(
    ICurrentUser currentUser, ISquadStore squads, ICoachStore coaches, IClubStore clubs, IInjuryStore injuries, IInjuryTypeStore types,
    IInjuryDocumentStore documents, IInjuryProposalStore proposals, IAlertStore alerts, IMatchStore matches, ITeamStore teams, ITrainingStore trainings,
    IUnitOfWork unitOfWork, TimeProvider clock, IBodySiteStore sites)
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
        var detail = await BodySiteCatalog.DetailAsync(sites, command.Site, command.Side, command.Grade, command.Mechanism, cancellationToken);
        var now = clock.GetUtcNow();
        var occasion = await InjuryOccasions.ResolveAsync(
            squad, command.MatchId, command.TrainingSessionId, injury.Occasion, matches, teams, trainings, now, cancellationToken);
        injury.Update(type, command.Reason, command.StartedOn, command.ExpectedReturnOn, now, occasion, detail);
        var editor = await Resolvers.CurrentEditorAsync(currentUser, coaches, clubs, cancellationToken);
        await InjuryOccasions.SyncProposalsAsync(squad, injury, editor, proposals, clubs, alerts, clock, cancellationToken);
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
    IInjuryProposalStore proposals, IAlertStore alerts, IMatchStore matches, ITeamStore teams, ITrainingStore trainings, IUnitOfWork unitOfWork,
    TimeProvider clock, IBodySiteStore sites)
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
        // Es la misma lesión que vuelve: si no se dice otra cosa, en el mismo sitio y del mismo lado.
        var detail = await BodySiteCatalog.DetailAsync(
            sites, command.Site ?? previous.SiteCode, command.Side ?? previous.Side, command.Grade, command.Mechanism, cancellationToken);
        var editor = await Resolvers.CurrentEditorAsync(currentUser, coaches, clubs, cancellationToken);
        var now = clock.GetUtcNow();
        var occasion = await InjuryOccasions.ResolveAsync(squad, command.MatchId, command.TrainingSessionId, null, matches, teams, trainings, now, cancellationToken);
        var relapse = PlayerInjury.Create(
            squad, player, editor, type, command.Reason, command.StartedOn, expected, now, relapseOf: previous, occasion: occasion, detail: detail);
        injuries.Add(relapse);
        await InjuryOccasions.SyncProposalsAsync(squad, relapse, editor, proposals, clubs, alerts, clock, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return await InjuryAccess.ListAsync(squad, injuries, documents, clock, cancellationToken);
    }
}

// Con la lesión se van sus pruebas e informes: primero de la base de datos y, ya guardado, los ficheros. Si pasó abierta a
// la temporada siguiente, la de allí se queda (con su parte), ya sin enlace; si es la continuación, la de la temporada
// anterior sigue como historial y no se vuelve a pasar.
internal sealed class DeleteInjuryHandler(
    ICurrentUser currentUser, ISquadStore squads, IClubStore clubs, IInjuryStore injuries, IInjuryDocumentStore documents,
    IInjuryDocumentStorage storage, IInjuryProposalStore proposals, IUnitOfWork unitOfWork, TimeProvider clock)
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
        // Si se registró desde la crónica de un partido, esa posible lesión queda descartada: no se vuelve a proponer.
        foreach (var proposal in (await proposals.GetBySquadAsync(squad.Id, cancellationToken)).Where(p => p.InjuryId == injury.Id))
            proposal.ForgetInjury(now);
        injuries.Remove(injury);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        foreach (var document in attached)
            await storage.DeleteAsync(document.StorageKey, CancellationToken.None);

        return await InjuryAccess.ListAsync(squad, injuries, documents, clock, cancellationToken);
    }
}
