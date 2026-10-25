using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Common;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Common.Errors;
using TercerTiempo.Domain.Competitions;

namespace TercerTiempo.Application.Seasons;

// Temporada que pide la petición (cabecera X-Season: su id o su nombre, "2026/2027" o "2026-2027"); null = la actual.
public interface ISeasonRequest
{
    string? Requested { get; }
}

// Sin petición HTTP (trabajos en segundo plano): la temporada actual.
internal sealed class NoSeasonRequest : ISeasonRequest
{
    public string? Requested => null;
}

// La temporada con la que se leen todos los datos de una petición. Las escrituras solo valen en la actual.
public interface ISeasonContext
{
    Task<SeasonYear?> SelectedAsync(CancellationToken cancellationToken = default);
    Task<SeasonYear?> CurrentAsync(CancellationToken cancellationToken = default);
    Task EnsureEditableAsync(CancellationToken cancellationToken = default);
}

internal sealed class SeasonContext(ISeasonYearStore store, ISeasonRequest request) : ISeasonContext
{
    private IReadOnlyList<SeasonYear>? _all;

    private async Task<IReadOnlyList<SeasonYear>> AllAsync(CancellationToken cancellationToken) =>
        _all ??= await store.GetAllAsync(cancellationToken);

    public async Task<SeasonYear?> CurrentAsync(CancellationToken cancellationToken = default)
    {
        var all = await AllAsync(cancellationToken);
        return all.FirstOrDefault(s => s.IsCurrent) ?? all.FirstOrDefault();
    }

    // Una temporada pedida que ya no existe (p. ej. guardada en el navegador) cae en la actual en lugar de fallar.
    public async Task<SeasonYear?> SelectedAsync(CancellationToken cancellationToken = default)
    {
        var requested = request.Requested?.Trim();
        if (string.IsNullOrEmpty(requested)) return await CurrentAsync(cancellationToken);

        var all = await AllAsync(cancellationToken);
        var label = requested.Replace('-', '/');
        return all.FirstOrDefault(s => Guid.TryParse(requested, out var id) && s.Id == id)
            ?? all.FirstOrDefault(s => s.Label.Equals(label, StringComparison.OrdinalIgnoreCase))
            ?? await CurrentAsync(cancellationToken);
    }

    public async Task EnsureEditableAsync(CancellationToken cancellationToken = default)
    {
        var selected = await SelectedAsync(cancellationToken);
        var current = await CurrentAsync(cancellationToken);
        if (selected is not null && current is not null && selected.Id != current.Id)
            throw new ConflictException(ErrorCode.SeasonClosed, selected.Label, current.Label);
    }
}

public sealed record SeasonYearDto(Guid Id, string Label, DateOnly StartsOn, DateOnly EndsOn, bool IsCurrent);

public sealed record ListSeasonsQuery : IQuery<IReadOnlyList<SeasonYearDto>>;

internal sealed class ListSeasonsHandler(ISeasonYearStore store) : IQueryHandler<ListSeasonsQuery, IReadOnlyList<SeasonYearDto>>
{
    public async Task<IReadOnlyList<SeasonYearDto>> Handle(ListSeasonsQuery query, CancellationToken cancellationToken) =>
        (await store.GetAllAsync(cancellationToken))
            .Select(s => new SeasonYearDto(s.Id, s.Label, s.StartsOn, s.EndsOn, s.IsCurrent))
            .ToList();
}

// Administración: qué temporada es la actual (la que se edita y la que ve quien no elige otra).
public sealed record SetCurrentSeasonCommand(Guid SeasonYearId) : ICommand<IReadOnlyList<SeasonYearDto>>;

internal sealed class SetCurrentSeasonHandler(ISeasonYearStore store, IUnitOfWork unitOfWork)
    : ICommandHandler<SetCurrentSeasonCommand, IReadOnlyList<SeasonYearDto>>
{
    public async Task<IReadOnlyList<SeasonYearDto>> Handle(SetCurrentSeasonCommand command, CancellationToken cancellationToken)
    {
        var all = await store.GetAllForUpdateAsync(cancellationToken);
        SeasonYear.MarkCurrent(all, command.SeasonYearId);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return all.Select(s => new SeasonYearDto(s.Id, s.Label, s.StartsOn, s.EndsOn, s.IsCurrent)).ToList();
    }
}
