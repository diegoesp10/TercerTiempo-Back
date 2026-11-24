using System.ComponentModel;
using FluentValidation;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Common;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Common.Errors;
using TercerTiempo.Domain.Squads;

namespace TercerTiempo.Application.Squads.Injuries;

public sealed record AdminInjuryTypeDto(
    string Code,
    string Label,
    [property: Description("Duración media orientativa en días; null si no hay referencia.")]
    int? AverageDays,
    int SortOrder,
    [property: Description("false = retirado: no se ofrece para lesiones nuevas, pero sigue en las que ya lo tienen.")]
    bool Active,
    [property: Description("Lesiones guardadas con este tipo (de todas las plantillas). Con alguna no se puede borrar.")]
    int Injuries,
    [property: Description("Clase de sitio que pregunta (\"qué músculo\", \"qué hueso\"...); null si tiene un sitio fijo o no pregunta dónde fue.")]
    BodySiteKind? SiteKind,
    [property: Description("Sitio fijo (código del catálogo de sitios del cuerpo): el tobillo en el esguince de tobillo; null si no tiene.")]
    string? BaseSite,
    [property: Description("Con sitio fijo, lo que lo precisa si se sabe; con clase, solo esos sitios (vacía: todos los de la clase).")]
    IReadOnlyList<string> Sites,
    [property: Description("true: se pregunta el grado (I, II o III).")]
    bool Graded,
    DateTimeOffset UpdatedAt);

public sealed record ListAdminInjuryTypesQuery : IQuery<IReadOnlyList<AdminInjuryTypeDto>>;

public sealed record SaveInjuryTypeCommand(
    [property: Description("El tipo que se cambia; al crear uno se ignora (el código sale del nombre).")]
    string? Code,
    string Label,
    [property: Description("De 1 a 730 días; null si no hay una referencia.")]
    int? AverageDays,
    [property: Description("Posición en la lista (de menor a mayor; a igualdad, por nombre).")]
    int SortOrder,
    [property: Description("false lo retira: deja de ofrecerse para lesiones nuevas.")]
    bool Active,
    [property: Description("Clase de sitio que pregunta; se ignora si hay sitio fijo. Sin clase ni sitio fijo no se pregunta dónde fue.")]
    BodySiteKind? SiteKind = null,
    [property: Description("Sitio fijo (código del catálogo de sitios del cuerpo); null si no tiene.")]
    string? BaseSite = null,
    [property: Description("Con sitio fijo, lo que lo precisa; con clase, solo esos sitios (vacía o null: todos los de la clase). Como mucho 60.")]
    IReadOnlyList<string>? Sites = null,
    [property: Description("true: se pregunta el grado (I distensión, II rotura parcial, III rotura completa).")]
    bool Graded = false) : ICommand<IReadOnlyList<AdminInjuryTypeDto>>;

public sealed record DeleteInjuryTypeCommand(string Code) : ICommand<IReadOnlyList<AdminInjuryTypeDto>>;

internal sealed class SaveInjuryTypeValidator : AbstractValidator<SaveInjuryTypeCommand>
{
    public SaveInjuryTypeValidator()
    {
        RuleFor(x => x.Label).NotEmpty().MaximumLength(InjuryTypeDefinition.LabelMaxLength).WithName("Nombre");
        RuleFor(x => x.AverageDays).InclusiveBetween(1, InjuryTypeDefinition.MaxAverageDays).When(x => x.AverageDays.HasValue)
            .WithCatalogMessage(ErrorCode.InjuryTypeAverageDaysInvalid, InjuryTypeDefinition.MaxAverageDays);
        RuleFor(x => x.SortOrder).InclusiveBetween(0, 1000).WithName("Orden");
        RuleFor(x => x.BaseSite).MaximumLength(BodySite.CodeMaxLength).WithName("Sitio fijo");
        RuleFor(x => x.Sites).Must(sites => sites is null || sites.Count <= InjuryTypeDefinition.MaxSites)
            .WithCatalogMessage(ErrorCode.InjuryTypeSitesTooMany, InjuryTypeDefinition.MaxSites);
        RuleForEach(x => x.Sites).NotEmpty().MaximumLength(BodySite.CodeMaxLength).WithName("Sitio del cuerpo");
    }
}

internal sealed class ListAdminInjuryTypesHandler(IInjuryTypeStore types) : IQueryHandler<ListAdminInjuryTypesQuery, IReadOnlyList<AdminInjuryTypeDto>>
{
    public async Task<IReadOnlyList<AdminInjuryTypeDto>> Handle(ListAdminInjuryTypesQuery query, CancellationToken cancellationToken)
    {
        var usage = await types.CountUsageAsync(cancellationToken);
        return InjuryTypeCatalog.Sorted(await types.GetAllAsync(cancellationToken))
            .Select(t => new AdminInjuryTypeDto(
                t.Code, t.Label, t.AverageDays, t.SortOrder, t.Active, usage.GetValueOrDefault(t.Code), t.SiteKind, t.BaseSiteCode, t.SiteCodes,
                t.Graded, t.UpdatedAt))
            .ToList();
    }
}

internal sealed class SaveInjuryTypeHandler(IInjuryTypeStore types, IBodySiteStore sites, IUnitOfWork unitOfWork, TimeProvider clock)
    : ICommandHandler<SaveInjuryTypeCommand, IReadOnlyList<AdminInjuryTypeDto>>
{
    public async Task<IReadOnlyList<AdminInjuryTypeDto>> Handle(SaveInjuryTypeCommand command, CancellationToken cancellationToken)
    {
        var all = await types.GetAllAsync(cancellationToken);
        var label = command.Label.Trim();
        var editing = command.Code is { } code
            ? all.FirstOrDefault(t => string.Equals(t.Code, code, StringComparison.OrdinalIgnoreCase)) ?? throw new NotFoundException(ErrorCode.InjuryTypeNotFound, code)
            : null;

        if (all.Any(t => t != editing && NameNormalizer.Normalize(t.Label) == NameNormalizer.Normalize(label)))
            throw new ConflictException(ErrorCode.InjuryTypeDuplicate, label);

        // Los sitios, del catálogo (también los retirados: un tipo que ya los tenía se sigue guardando igual).
        var anatomy = (await sites.GetAllAsync(cancellationToken)).ToDictionary(s => s.Code, StringComparer.OrdinalIgnoreCase);
        BodySite Site(string siteCode) => anatomy.GetValueOrDefault(siteCode) ?? throw new DomainException(ErrorCode.InjurySiteNotFound, siteCode);
        var baseSite = string.IsNullOrWhiteSpace(command.BaseSite) ? null : Site(command.BaseSite.Trim());
        var listed = (command.Sites ?? [])
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(Site)
            .ToList();

        var now = clock.GetUtcNow();
        var type = editing ?? InjuryTypeDefinition.Create(
            CatalogCode.From(label, all.Select(t => t.Code), InjuryTypeDefinition.CodeMaxLength, "Tipo"), label, command.AverageDays, command.SortOrder, now);
        if (editing is not null)
            type.Update(label, command.AverageDays, command.SortOrder, command.Active, now);
        // Lo que pregunta, antes de añadirlo: si no encaja, el tipo nuevo no llega al catálogo.
        type.SetLocation(command.SiteKind, baseSite, listed, command.Graded, now);
        if (editing is null)
            types.Add(type);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await new ListAdminInjuryTypesHandler(types).Handle(new ListAdminInjuryTypesQuery(), cancellationToken);
    }
}

internal sealed class DeleteInjuryTypeHandler(IInjuryTypeStore types, IUnitOfWork unitOfWork)
    : ICommandHandler<DeleteInjuryTypeCommand, IReadOnlyList<AdminInjuryTypeDto>>
{
    public async Task<IReadOnlyList<AdminInjuryTypeDto>> Handle(DeleteInjuryTypeCommand command, CancellationToken cancellationToken)
    {
        var type = await types.GetByCodeAsync(command.Code, cancellationToken)
            ?? throw new NotFoundException(ErrorCode.InjuryTypeNotFound, command.Code);

        var used = (await types.CountUsageAsync(cancellationToken)).GetValueOrDefault(type.Code);
        if (used > 0)
            throw new ConflictException(ErrorCode.InjuryTypeInUse, type.Label, used);

        types.Remove(type);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await new ListAdminInjuryTypesHandler(types).Handle(new ListAdminInjuryTypesQuery(), cancellationToken);
    }
}
