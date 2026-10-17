using System.Net;
using System.Text.RegularExpressions;
using TercerTiempo.Domain.Competitions;

namespace TercerTiempo.Infrastructure.FootballData.FutbolAsturiano;

/// <summary>Cómo se interpreta un término de liga de futbolasturiano.es.</summary>
public sealed record LeagueInfo(string DisplayName, string Category, CompetitionLevel Level, int Tier, string? Group);

/// <summary>
/// Traduce el nombre de una liga tal y como lo publica futbolasturiano.es ("3ª RFEF", "2ª AsturFútbol - Grupo 1",
/// "Copa AsturFútbol 2026 · Grupo A"...) a nivel, categoría, escalón y grupo.
/// </summary>
public static partial class LeagueClassifier
{
    [GeneratedRegex(@"^(?<tier>\d)ª\s*(?<cat>RFEF|AsturF[uú]tbol)(?:\s*[-–·]\s*Grupo\s*(?<grp>\w+))?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PyramidPattern();

    [GeneratedRegex(@"^(?<cat>.+?)\s*[-–·]\s*Grupo\s*(?<grp>\w+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex GroupSuffixPattern();

    public static LeagueInfo Classify(string rawName)
    {
        var name = Normalize(rawName);

        var pyramid = PyramidPattern().Match(name);
        if (pyramid.Success)
        {
            var tier = int.Parse(pyramid.Groups["tier"].Value);
            var isNational = pyramid.Groups["cat"].Value.Equals("RFEF", StringComparison.OrdinalIgnoreCase);
            var category = isNational ? $"{tier}ª RFEF" : $"{tier}ª AsturFútbol";
            var group = pyramid.Groups["grp"].Success ? pyramid.Groups["grp"].Value : null;

            return new LeagueInfo(WithGroup(category, group), category, isNational ? CompetitionLevel.National : CompetitionLevel.Regional, tier, group);
        }

        if (name.Equals("Primera División", StringComparison.OrdinalIgnoreCase) || name.Equals("Primera", StringComparison.OrdinalIgnoreCase))
            return new LeagueInfo("Primera División", "Primera División", CompetitionLevel.Professional, 1, null);

        if (name.Equals("Segunda División", StringComparison.OrdinalIgnoreCase) || name.Equals("Segunda", StringComparison.OrdinalIgnoreCase))
            return new LeagueInfo("Segunda División", "Segunda División", CompetitionLevel.Professional, 2, null);

        var (baseName, suffixGroup) = SplitGroup(name);

        if (baseName.Contains("copa", StringComparison.OrdinalIgnoreCase))
            return new LeagueInfo(WithGroup(baseName, suffixGroup), baseName, CompetitionLevel.Cup, 0, suffixGroup);

        if (baseName.Contains("juvenil", StringComparison.OrdinalIgnoreCase)
            || baseName.Contains("cadete", StringComparison.OrdinalIgnoreCase)
            || baseName.Contains("infantil", StringComparison.OrdinalIgnoreCase)
            || baseName.Contains("base", StringComparison.OrdinalIgnoreCase))
            return new LeagueInfo(WithGroup(baseName, suffixGroup), baseName, CompetitionLevel.Youth, 0, suffixGroup);

        return new LeagueInfo(WithGroup(baseName, suffixGroup), baseName, CompetitionLevel.Other, 0, suffixGroup);
    }

    private static (string BaseName, string? Group) SplitGroup(string name)
    {
        var match = GroupSuffixPattern().Match(name);
        return match.Success ? (match.Groups["cat"].Value.Trim(), match.Groups["grp"].Value) : (name, null);
    }

    private static string WithGroup(string category, string? group) =>
        group is null ? category : $"{category} · Grupo {group}";

    private static string Normalize(string raw) =>
        Regex.Replace(WebUtility.HtmlDecode(raw ?? string.Empty), @"\s+", " ").Trim();
}
