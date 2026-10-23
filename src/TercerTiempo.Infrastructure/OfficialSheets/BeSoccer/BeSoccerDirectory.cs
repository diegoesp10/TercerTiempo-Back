using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TercerTiempo.Application.Abstractions.OfficialSheets;
using TercerTiempo.Domain.OfficialSheets;
using TercerTiempo.Infrastructure.OfficialSheets.Asturfutbol;

namespace TercerTiempo.Infrastructure.OfficialSheets.BeSoccer;

// es.besoccer.com: /competicion/equipos/{competición}/{año}/{grupo} lista los equipos, /equipo/partidos/{equipo} sus
// partidos y /partido/{local}/{visitante}/{id}/alineaciones y /eventos el acta.
internal sealed class BeSoccerDirectory(
    BeSoccerClient client,
    IOptions<OfficialSheetOptions> options,
    ILogger<BeSoccerDirectory> logger) : IMatchSheetDirectory
{
    public const string Name = "besoccer";

    private readonly BeSoccerOptions _settings = options.Value.BeSoccer;

    public string ProviderName => Name;

    public string SheetUrl(string matchCode) => client.AbsoluteUrl($"partido/{matchCode}");

    public async Task<IReadOnlyList<string>> GetGroupsAsync(string category, string seasonLabel, CancellationToken cancellationToken)
    {
        if (CompetitionPath(category, seasonLabel) is not { } path) return [];

        var html = await client.GetHtmlAsync($"competicion/equipos/{path}", Catalog, cancellationToken);
        var groups = html is null ? [] : BeSoccerPages.ParseGroups(html);
        if (groups.Count == 0) logger.LogInformation("BeSoccer: sin equipos en {Path}", path);
        return groups;
    }

    public async Task<IReadOnlyList<DirectoryTeam>> GetTeamsAsync(string category, string seasonLabel, string group, CancellationToken cancellationToken)
    {
        if (CompetitionPath(category, seasonLabel) is not { } path) return [];

        var html = await client.GetHtmlAsync($"competicion/equipos/{path}{(group.Length > 0 ? "/" + group : string.Empty)}", Catalog, cancellationToken);
        return html is null ? [] : BeSoccerPages.ParseTeams(html);
    }

    public async Task<IReadOnlyList<DirectoryFixture>> GetFixturesAsync(string teamCode, CancellationToken cancellationToken)
    {
        var html = await client.GetHtmlAsync($"equipo/partidos/{teamCode}", TimeSpan.FromHours(Math.Max(1, _settings.FixturesCacheHours)), cancellationToken);
        return html is null ? [] : BeSoccerPages.ParseFixtures(html);
    }

    public async Task<OfficialSheetData?> GetSheetAsync(string matchCode, CancellationToken cancellationToken)
    {
        var sheetCache = TimeSpan.FromHours(Math.Max(1, _settings.SheetCacheHours));
        var lineups = await client.GetHtmlAsync($"partido/{matchCode}/alineaciones", sheetCache, cancellationToken);
        if (lineups is null) return null;

        var data = BeSoccerPages.ParseSheet(lineups, null, _settings.MatchLength);
        // Sin alineaciones no hay acta: no merece la pena pedir los eventos.
        if (data is null || data.Lines.Count == 0) return data;

        var events = await client.GetHtmlAsync($"partido/{matchCode}/eventos", sheetCache, cancellationToken);
        return BeSoccerPages.ParseSheet(lineups, events, _settings.MatchLength);
    }

    private TimeSpan Catalog => TimeSpan.FromDays(Math.Max(1, _settings.CatalogCacheDays));

    // "2026/2027" → "{competición}/2027".
    private string? CompetitionPath(string category, string seasonLabel)
    {
        if (!_settings.Competitions.TryGetValue(category, out var slug) || string.IsNullOrWhiteSpace(slug)) return null;

        var digits = new string(seasonLabel.Where(char.IsDigit).ToArray());
        var year = digits.Length >= 8 ? digits[^4..] : digits.Length == 6 ? "20" + digits[^2..] : null;
        return year is null ? null : $"{slug.Trim('/')}/{year}";
    }
}
