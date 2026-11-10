using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TercerTiempo.Application.Abstractions.OfficialSheets;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.OfficialSheets;

namespace TercerTiempo.Infrastructure.OfficialSheets.Asturfutbol;

internal sealed class AsturfutbolOfficialSheetProvider(
    AsturfutbolClient client,
    IOptions<OfficialSheetOptions> options,
    ILogger<AsturfutbolOfficialSheetProvider> logger) : IOfficialSheetProvider
{
    public const string Name = "asturfutbol";

    private static readonly Dictionary<string, string[]> CategoryAliases = new()
    {
        ["3ª rfef"] = ["tercera federacion", "tercera division"],
    };

    // La 1ª y la 2ª RFEF las organiza la RFEF: no están en la web de la federación asturiana (comprobado el 26-10-2026).
    private static readonly HashSet<string> NotPublished = ["1ª rfef", "2ª rfef"];

    private readonly AsturfutbolOptions _settings = options.Value.Asturfutbol;

    private IReadOnlyList<CatalogEntry>? _seasons;
    private readonly Dictionary<string, IReadOnlyList<CatalogEntry>> _competitionsBySeason = [];

    public string ProviderName => Name;

    public bool Publishes(string category) => !NotPublished.Contains(NameNormalizer.Normalize(category));

    public string SheetUrl(string sheetCode) =>
        client.AbsoluteUrl($"NFG_CmpPartido?cod_primaria={_settings.CalendarCode}&CodActa={sheetCode}&cod_acta={sheetCode}");

    public async Task<OfficialCompetitionRef?> FindCompetitionAsync(string category, string? group, string seasonLabel, CancellationToken cancellationToken)
    {
        var seasons = await GetSeasonsAsync(cancellationToken);
        var season = seasons.FirstOrDefault(s => SeasonKey(s.Name) == SeasonKey(seasonLabel));
        if (season is null)
        {
            logger.LogInformation("asturfutbol.es: temporada {Season} no encontrada", seasonLabel);
            return null;
        }

        var competitions = await GetCompetitionsAsync(season.Code, cancellationToken);
        var competition = competitions.FirstOrDefault(c => Matches(c.Name, category));
        if (competition is null)
        {
            logger.LogInformation("asturfutbol.es: competición {Category} no encontrada en {Season}", category, seasonLabel);
            return null;
        }

        var groups = await GetGroupsAsync(competition.Code, cancellationToken);
        var chosen = groups.Count == 1
            ? groups[0]
            : groups.FirstOrDefault(g => GroupKey(g.Name) == GroupKey(group ?? string.Empty))
              ?? (string.IsNullOrEmpty(group) ? groups.FirstOrDefault() : null);

        if (chosen is null)
        {
            logger.LogInformation("asturfutbol.es: grupo {Group} de {Category} no encontrado (hay {Count})", group, category, groups.Count);
            return null;
        }

        return new OfficialCompetitionRef(competition.Code, chosen.Code, season.Code);
    }

    public async Task<IReadOnlyList<OfficialFixture>> GetMatchdayAsync(OfficialCompetitionRef competition, int matchday, CancellationToken cancellationToken)
    {
        var html = await client.GetHtmlAsync(
            $"NFG_CmpJornada?cod_primaria={_settings.CalendarCode}&CodCompeticion={competition.CompetitionCode}&CodGrupo={competition.GroupCode}&CodTemporada={competition.SeasonCode}&cod_agrupacion=1&CodJornada={matchday}",
            cancellationToken);

        return html is null ? [] : AsturfutbolPages.ParseMatchday(html);
    }

    public async Task<OfficialSheetData?> GetCachedSheetAsync(string sheetCode, CancellationToken cancellationToken)
    {
        var html = await client.PeekHtmlAsync(SheetPath(sheetCode), cancellationToken);
        return html is null ? null : AsturfutbolPages.ParseSheet(html, _settings.MatchLength);
    }

    private string SheetPath(string sheetCode) => $"NFG_CmpPartido?cod_primaria={_settings.CalendarCode}&CodActa={sheetCode}&cod_acta={sheetCode}";

    public async Task<OfficialSheetData?> GetSheetAsync(string sheetCode, CancellationToken cancellationToken)
    {
        var html = await client.GetHtmlAsync(SheetPath(sheetCode), cancellationToken);

        return html is null ? null : AsturfutbolPages.ParseSheet(html, _settings.MatchLength);
    }

    private async Task<IReadOnlyList<CatalogEntry>> GetSeasonsAsync(CancellationToken cancellationToken)
    {
        if (_seasons is not null) return _seasons;

        var html = await client.GetHtmlAsync($"NFG_CmpJornada?cod_primaria={_settings.CalendarCode}", cancellationToken) ?? string.Empty;
        return _seasons = AsturfutbolPages.ParseSelect(html, "temporada");
    }

    private async Task<IReadOnlyList<CatalogEntry>> GetCompetitionsAsync(string seasonCode, CancellationToken cancellationToken)
    {
        if (_competitionsBySeason.TryGetValue(seasonCode, out var cached)) return cached;

        var script = await client.GetHtmlAsync($"NFG_CmpJornada_Exec?cod_primaria={_settings.CalendarCode}&codtemporada={seasonCode}", cancellationToken) ?? string.Empty;
        var list = AsturfutbolPages.ParseJsArray(script);

        // La página inicial ya trae las competiciones de la temporada en curso; sirve de respaldo si el script cambia.
        if (list.Count == 0)
        {
            var html = await client.GetHtmlAsync($"NFG_CmpJornada?cod_primaria={_settings.CalendarCode}", cancellationToken) ?? string.Empty;
            list = AsturfutbolPages.ParseSelect(html, "competicion");
        }

        return _competitionsBySeason[seasonCode] = list;
    }

    private async Task<IReadOnlyList<CatalogEntry>> GetGroupsAsync(string competitionCode, CancellationToken cancellationToken)
    {
        var script = await client.GetHtmlAsync($"NFG_CmpJornada_Exec?cod_primaria={_settings.CalendarCode}&codcompeticion={competitionCode}", cancellationToken) ?? string.Empty;
        return AsturfutbolPages.ParseJsArray(script);
    }

    // ------------------------------------------------------------------ nombres

    internal static string SeasonKey(string label)
    {
        var digits = new string(label.Where(char.IsDigit).ToArray());
        return digits.Length == 6 ? digits[..4] + "20" + digits[4..] : digits;
    }

    internal static string GroupKey(string name)
    {
        var key = NameNormalizer.Normalize(name).Replace("grupo", string.Empty).Trim();
        return key.Length == 0 ? string.Empty : key;
    }

    internal static bool Matches(string federationName, string category)
    {
        var theirs = NameNormalizer.Normalize(federationName).Replace("- ", string.Empty).Trim();
        var ours = NameNormalizer.Normalize(category);

        if (theirs == ours) return true;

        return CategoryAliases.TryGetValue(ours, out var aliases) && aliases.Any(a => theirs.StartsWith(a, StringComparison.Ordinal));
    }
}
