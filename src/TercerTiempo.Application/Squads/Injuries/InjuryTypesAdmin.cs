using System.ComponentModel;
using System.Text.RegularExpressions;
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
    bool Active) : ICommand<IReadOnlyList<AdminInjuryTypeDto>>;

public sealed record DeleteInjuryTypeCommand(string Code) : ICommand<IReadOnlyList<AdminInjuryTypeDto>>;

internal sealed class SaveInjuryTypeValidator : AbstractValidator<SaveInjuryTypeCommand>
{
    public SaveInjuryTypeValidator()
    {
        RuleFor(x => x.Label).NotEmpty().MaximumLength(InjuryTypeDefinition.LabelMaxLength).WithName("Nombre");
        RuleFor(x => x.AverageDays).InclusiveBetween(1, InjuryTypeDefinition.MaxAverageDays).When(x => x.AverageDays.HasValue)
            .WithCatalogMessage(ErrorCode.InjuryTypeAverageDaysInvalid, InjuryTypeDefinition.MaxAverageDays);
        RuleFor(x => x.SortOrder).InclusiveBetween(0, 1000).WithName("Orden");
    }
}

internal sealed class ListAdminInjuryTypesHandler(IInjuryTypeStore types) : IQueryHandler<ListAdminInjuryTypesQuery, IReadOnlyList<AdminInjuryTypeDto>>
{
    public async Task<IReadOnlyList<AdminInjuryTypeDto>> Handle(ListAdminInjuryTypesQuery query, CancellationToken cancellationToken)
    {
        var usage = await types.CountUsageAsync(cancellationToken);
        return InjuryTypeCatalog.Sorted(await types.GetAllAsync(cancellationToken))
            .Select(t => new AdminInjuryTypeDto(t.Code, t.Label, t.AverageDays, t.SortOrder, t.Active, usage.GetValueOrDefault(t.Code), t.UpdatedAt))
            .ToList();
    }
}

internal sealed partial class SaveInjuryTypeHandler(IInjuryTypeStore types, IUnitOfWork unitOfWork, TimeProvider clock)
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

        var now = clock.GetUtcNow();
        if (editing is not null)
            editing.Update(label, command.AverageDays, command.SortOrder, command.Active, now);
        else
            types.Add(InjuryTypeDefinition.Create(NewCode(label, all), label, command.AverageDays, command.SortOrder, now));

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await new ListAdminInjuryTypesHandler(types).Handle(new ListAdminInjuryTypesQuery(), cancellationToken);
    }

    // El código no lo ve nadie: sale del nombre («Rotura de menisco» → RoturaDeMenisco) y no cambia aunque se renombre,
    // porque es lo que guardan las lesiones.
    private static string NewCode(string label, IEnumerable<InjuryTypeDefinition> existing)
    {
        var taken = existing.Select(t => t.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var stem = string.Concat(NonAlphanumeric().Split(NameNormalizer.Normalize(label))
            .Where(word => word.Length > 0)
            .Select(word => char.ToUpperInvariant(word[0]) + word[1..]));
        if (stem.Length == 0) stem = "Tipo";
        stem = stem[..Math.Min(stem.Length, InjuryTypeDefinition.CodeMaxLength - 3)];

        var candidate = stem;
        for (var n = 2; taken.Contains(candidate); n++)
            candidate = $"{stem}{n}";
        return candidate;
    }

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonAlphanumeric();
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
