using System.ComponentModel;
using FluentValidation;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Common;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Common.Errors;
using TercerTiempo.Domain.Squads;

namespace TercerTiempo.Application.Squads.Injuries;

public sealed record AdminBodySiteDto(
    string Code,
    string Label,
    BodySiteKind Kind,
    BodyRegion Region,
    [property: Description("Grupo al que pertenece (código de otro sitio de la misma clase); null si no está en ninguno.")]
    string? Parent,
    [property: Description("true: hay izquierdo y derecho, y se pregunta el lado.")]
    bool Paired,
    [property: Description("Cómo concuerda el lado con su nombre: «rodilla izquierda», «isquiotibiales izquierdos»...")]
    BodySiteAgreement Agreement,
    int SortOrder,
    [property: Description("false = retirado: no se ofrece para lesiones nuevas, pero sigue en las que ya lo tienen.")]
    bool Active,
    [property: Description("Lesiones guardadas en este sitio (de todas las plantillas).")]
    int Injuries,
    [property: Description("Tipos de lesión que lo tienen como sitio fijo o en su lista.")]
    int Types,
    [property: Description("Sitios de su grupo, si es un grupo.")]
    int Children,
    DateTimeOffset UpdatedAt);

public sealed record ListAdminBodySitesQuery : IQuery<IReadOnlyList<AdminBodySiteDto>>;

public sealed record SaveBodySiteCommand(
    [property: Description("El sitio que se cambia; al crear uno se ignora (el código sale del nombre).")]
    string? Code,
    string Label,
    BodySiteKind Kind,
    BodyRegion Region,
    [property: Description("Grupo: otro sitio de la misma clase que no esté a su vez en un grupo; null si ninguno.")]
    string? Parent,
    [property: Description("true: hay izquierdo y derecho, y se pregunta el lado.")]
    bool Paired,
    [property: Description("Cómo concuerda el lado con su nombre: Masculine, Feminine, MasculinePlural o FemininePlural.")]
    BodySiteAgreement Agreement,
    [property: Description("Posición dentro de su zona del cuerpo (de menor a mayor; a igualdad, por nombre).")]
    int SortOrder,
    [property: Description("false lo retira: deja de ofrecerse para lesiones nuevas.")]
    bool Active) : ICommand<IReadOnlyList<AdminBodySiteDto>>;

public sealed record DeleteBodySiteCommand(string Code) : ICommand<IReadOnlyList<AdminBodySiteDto>>;

internal sealed class SaveBodySiteValidator : AbstractValidator<SaveBodySiteCommand>
{
    public SaveBodySiteValidator()
    {
        RuleFor(x => x.Label).NotEmpty().MaximumLength(BodySite.LabelMaxLength).WithName("Nombre");
        RuleFor(x => x.Parent).MaximumLength(BodySite.CodeMaxLength).WithName("Grupo");
        RuleFor(x => x.SortOrder).InclusiveBetween(0, 10000).WithName("Orden");
    }
}

internal sealed class ListAdminBodySitesHandler(IBodySiteStore sites, IInjuryTypeStore types)
    : IQueryHandler<ListAdminBodySitesQuery, IReadOnlyList<AdminBodySiteDto>>
{
    public async Task<IReadOnlyList<AdminBodySiteDto>> Handle(ListAdminBodySitesQuery query, CancellationToken cancellationToken)
    {
        var all = await sites.GetAllAsync(cancellationToken);
        var injuries = await sites.CountUsageAsync(cancellationToken);
        var offered = BodySiteUsage.ByTypes(await types.GetAllAsync(cancellationToken));
        var children = all.Where(s => s.ParentCode is not null).GroupBy(s => s.ParentCode!).ToDictionary(g => g.Key, g => g.Count());
        return BodySiteCatalog.Sorted(all)
            .Select(s => new AdminBodySiteDto(
                s.Code, s.Label, s.Kind, s.Region, s.ParentCode, s.Paired, s.Agreement, s.SortOrder, s.Active, injuries.GetValueOrDefault(s.Code),
                offered.GetValueOrDefault(s.Code), children.GetValueOrDefault(s.Code), s.UpdatedAt))
            .ToList();
    }
}

internal static class BodySiteUsage
{
    // Tipos que tienen cada sitio como fijo o en su lista.
    public static IReadOnlyDictionary<string, int> ByTypes(IEnumerable<InjuryTypeDefinition> types) =>
        types.SelectMany(t => t.SiteCodes.Append(t.BaseSiteCode).OfType<string>().Distinct(StringComparer.Ordinal))
            .GroupBy(code => code, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
}

internal sealed class SaveBodySiteHandler(IBodySiteStore sites, IInjuryTypeStore types, IUnitOfWork unitOfWork, TimeProvider clock)
    : ICommandHandler<SaveBodySiteCommand, IReadOnlyList<AdminBodySiteDto>>
{
    public async Task<IReadOnlyList<AdminBodySiteDto>> Handle(SaveBodySiteCommand command, CancellationToken cancellationToken)
    {
        var all = await sites.GetAllAsync(cancellationToken);
        var label = command.Label.Trim();
        var editing = command.Code is { } code
            ? all.FirstOrDefault(s => string.Equals(s.Code, code, StringComparison.OrdinalIgnoreCase)) ?? throw new NotFoundException(ErrorCode.BodySiteNotFound, code)
            : null;

        if (all.Any(s => s != editing && NameNormalizer.Normalize(s.Label) == NameNormalizer.Normalize(label)))
            throw new ConflictException(ErrorCode.BodySiteDuplicate, label);

        // Un grupo y los suyos son de la misma clase y solo hay un nivel: el grupo no está en otro, y un sitio que ya es
        // grupo de otros ni entra en uno ni cambia de clase.
        var hasChildren = editing is not null && all.Any(s => s.ParentCode == editing.Code);
        BodySite? parent = null;
        if (!string.IsNullOrWhiteSpace(command.Parent))
        {
            parent = all.FirstOrDefault(s => string.Equals(s.Code, command.Parent.Trim(), StringComparison.OrdinalIgnoreCase));
            if (parent is null || parent == editing || parent.Kind != command.Kind || parent.ParentCode is not null || hasChildren)
                throw new DomainException(ErrorCode.BodySiteParentInvalid);
        }
        if (hasChildren && editing!.Kind != command.Kind)
            throw new DomainException(ErrorCode.BodySiteParentInvalid);

        var now = clock.GetUtcNow();
        if (editing is not null)
            editing.Update(label, command.Kind, command.Region, parent?.Code, command.Paired, command.Agreement, command.SortOrder, command.Active, now);
        else
            sites.Add(BodySite.Create(
                CatalogCode.From(label, all.Select(s => s.Code), BodySite.CodeMaxLength, "Sitio"), label, command.Kind, command.Region, parent?.Code,
                command.Paired, command.Agreement, command.SortOrder, now));

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await new ListAdminBodySitesHandler(sites, types).Handle(new ListAdminBodySitesQuery(), cancellationToken);
    }
}

internal sealed class DeleteBodySiteHandler(IBodySiteStore sites, IInjuryTypeStore types, IUnitOfWork unitOfWork)
    : ICommandHandler<DeleteBodySiteCommand, IReadOnlyList<AdminBodySiteDto>>
{
    public async Task<IReadOnlyList<AdminBodySiteDto>> Handle(DeleteBodySiteCommand command, CancellationToken cancellationToken)
    {
        var all = await sites.GetAllAsync(cancellationToken);
        var site = all.FirstOrDefault(s => string.Equals(s.Code, command.Code, StringComparison.OrdinalIgnoreCase))
            ?? throw new NotFoundException(ErrorCode.BodySiteNotFound, command.Code);

        // Lo que lo usa se queda como está: con algo, no se borra (se retira).
        var injuries = (await sites.CountUsageAsync(cancellationToken)).GetValueOrDefault(site.Code);
        var offered = BodySiteUsage.ByTypes(await types.GetAllAsync(cancellationToken)).GetValueOrDefault(site.Code);
        var children = all.Count(s => s.ParentCode == site.Code);
        var uses = new List<string>();
        if (injuries > 0) uses.Add(injuries == 1 ? "1 lesión" : $"{injuries} lesiones");
        if (offered > 0) uses.Add(offered == 1 ? "1 tipo de lesión" : $"{offered} tipos de lesión");
        if (children > 0) uses.Add(children == 1 ? "1 sitio de su grupo" : $"{children} sitios de su grupo");
        if (uses.Count > 0)
            throw new ConflictException(ErrorCode.BodySiteInUse, site.Label, Join(uses));

        sites.Remove(site);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await new ListAdminBodySitesHandler(sites, types).Handle(new ListAdminBodySitesQuery(), cancellationToken);
    }

    private static string Join(IReadOnlyList<string> parts) =>
        parts.Count == 1 ? parts[0] : $"{string.Join(", ", parts.Take(parts.Count - 1))} y {parts[^1]}";
}
