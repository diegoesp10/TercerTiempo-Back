using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using TercerTiempo.Application.Abstractions.OfficialSheets;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Competitions;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.OfficialSheets;
using TercerTiempo.Domain.Teams;

namespace TercerTiempo.Application.OfficialSheets;

public sealed class PublicSheetOptions
{
    public const string SectionName = "OfficialSheets";

    public static readonly string[] DefaultSources = ["asturfutbol"];

    // Webs de las que se saca el acta pública, por orden; hoy solo la federación. Vacío = DefaultSources (el enlazado
    // de configuración añade a un array con valores en lugar de sustituirlos). Las actas guardadas de una web que ya no
    // está aquí se ocultan.
    public string[] Sources { get; set; } = [];

    public IReadOnlyList<string> EnabledSources =>
        (Sources is { Length: > 0 } ? Sources : DefaultSources).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    // Las actas guardadas de una web que se quita de Sources dejan de mostrarse.
    public bool Allows(string provider) => EnabledSources.Contains(provider, StringComparer.OrdinalIgnoreCase);
}

internal sealed record SheetFetch(string Provider, string ExternalId, string? SourceUrl, OfficialSheetData? Data)
{
    public bool Complete => Data is { Lines.Count: > 0 };
}

// Una web de la que se puede sacar el acta pública de un partido. null si no tiene ese partido.
internal interface IPublicSheetSource
{
    string Name { get; }

    // La federación solo se consulta para los partidos que importan (equipos con plantilla o que alguien abre).
    bool IsFederation { get; }

    Task<SheetFetch?> FetchAsync(Match match, Competition competition, Season season, OfficialMatchSheet? current, CancellationToken cancellationToken);
}

internal sealed class FederationSheetSource(
    IOfficialSheetProvider provider,
    OfficialSheetLocator locator,
    OfficialSheetSyncMemory memory,
    TimeProvider clock) : IPublicSheetSource
{
    public string Name => provider.ProviderName;

    public bool IsFederation => true;

    public async Task<SheetFetch?> FetchAsync(Match match, Competition competition, Season season, OfficialMatchSheet? current, CancellationToken cancellationToken)
    {
        var code = current?.Provider == Name ? current.ExternalId : null;
        if (code is null)
        {
            var now = clock.GetUtcNow();
            if (memory.SkipSeason(Name, season.Id, now)) return null;
            if (await locator.ResolveCompetitionAsync(competition, season, cancellationToken) is null)
            {
                memory.SeasonNotFound(Name, season.Id, now);
                return null;
            }

            code = (await locator.LocateAsync(match, competition, season, cancellationToken))?.ExternalId;
            if (code is null) return null;
        }

        return new SheetFetch(Name, code, provider.SheetUrl(code), await provider.GetSheetAsync(code, cancellationToken));
    }
}
