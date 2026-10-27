using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using TercerTiempo.Domain.Competitions;

namespace TercerTiempo.Infrastructure.FootballData.FutbolAsturiano;

// Plazas destacadas de una clasificación de futbolasturiano.es (SportsPress «highlight places»). La API no las da: están en
// la página de la tabla, como un estilo por fila (`table.sp-league-table-{id} tr.sp-row-no-{n} td.data-rank{ background: #… }`,
// con n desde 0) y una leyenda con el color y su significado (`.sp_highlight_places`).
internal static partial class LeagueTablePage
{
    public static IReadOnlyList<LeagueZoneData> ParseZones(string html, int tableId)
    {
        var labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match legend in Legend().Matches(html))
            labels.TryAdd(Hex(legend.Groups["color"].Value), WebUtility.HtmlDecode(legend.Groups["label"].Value).Trim());

        var rows = new Regex(
            $@"table\.sp-league-table-{tableId}\s+tr\.sp-row-no-(?<row>\d+)\s+td\.data-rank\s*\{{\s*background(?:-color)?\s*:\s*(?<color>#[0-9a-fA-F]{{3,6}})",
            RegexOptions.CultureInvariant);

        var zones = new Dictionary<int, LeagueZoneData>();
        foreach (Match row in rows.Matches(html))
        {
            var position = int.Parse(row.Groups["row"].Value, CultureInfo.InvariantCulture) + 1;
            var color = Hex(row.Groups["color"].Value);
            if (labels.TryGetValue(color, out var label) && label.Length > 0)
                zones[position] = new LeagueZoneData(position, label, color);
        }
        return zones.Values.OrderBy(z => z.Position).ToList();
    }

    // #abc → #aabbcc, en minúsculas.
    private static string Hex(string color)
    {
        var value = color.Trim().TrimStart('#').ToLowerInvariant();
        if (value.Length == 3) value = string.Concat(value.Select(c => $"{c}{c}"));
        return $"#{value}";
    }

    [GeneratedRegex(@"class=""sp_color""\s+style=""background-color:\s*(?<color>#[0-9a-fA-F]{3,6})[^""]*"">.*?</div>\s*<div\s+class=""sp_desc"">(?<label>[^<]*)</div>",
        RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex Legend();
}
