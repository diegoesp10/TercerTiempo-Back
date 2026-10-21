using System.Globalization;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using TercerTiempo.Application.Abstractions.Teams;
using TercerTiempo.Domain.Teams;

namespace TercerTiempo.Infrastructure.OfficialSheets.Asturfutbol;

/// <summary>Dirección del campo según su página (<c>NFG_VisCampos</c>).</summary>
internal sealed record GroundInfo(string? Address, string? Locality, string? Surface);

/// <summary>
/// Páginas de equipo y campo de asturfutbol.es:
///  - <c>NFG_VisEquipos?Codigo_Equipo=</c>: escudo, nombre oficial, sede, web, equipación y terreno de juego (con enlace al campo).
///  - <c>NFG_VisCampos?Codigo_Campo=</c>: dirección, localidad y superficie del campo.
/// Los datos van como <c>&lt;h5&gt;&lt;strong&gt;Etiqueta:&lt;/strong&gt; valor&lt;/h5&gt;</c>.
/// </summary>
internal static partial class AsturfutbolPages
{
    [GeneratedRegex(@"Codigo_Campo=(?<code>\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex GroundCodePattern();

    [GeneratedRegex(@"^(?<name>[^(]+?)\s*\((?<surface>[^)]+)\)\s*$")]
    private static partial Regex GroundWithSurfacePattern();

    /// <summary>Superficies tal y como las abrevia la federación en el nombre del campo.</summary>
    private static readonly Dictionary<string, string> Surfaces = new(StringComparer.OrdinalIgnoreCase)
    {
        ["H.SINTETICA"] = "Hierba artificial",
        ["H.ARTIFICIAL"] = "Hierba artificial",
        ["H.NATURAL"] = "Hierba natural",
        ["HIERBA"] = "Hierba natural",
        ["TIERRA"] = "Tierra",
    };

    /// <summary>Ficha del equipo; null si la página no tiene la cabecera del club (equipo inexistente).</summary>
    public static (OfficialTeamProfile Profile, string? GroundCode)? ParseTeamProfile(string html)
    {
        var document = Parser.ParseDocument(html);

        var crest = document.QuerySelector("img[src*='/pimg/Clubes/']")?.GetAttribute("src");
        var officialName = NullIfEmpty(Clean(document.QuerySelector(".titulo_cab_programa")?.TextContent ?? string.Empty));
        var fields = Fields(document);

        if (crest is null && officialName is null && fields.Count == 0)
            return null;

        var groundLink = document.QuerySelectorAll("h5").FirstOrDefault(h => Label(h).StartsWith("terreno de juego", StringComparison.OrdinalIgnoreCase))?.QuerySelector("a");
        var groundCode = groundLink is null ? null : NullIfEmpty(GroundCodePattern().Match(groundLink.GetAttribute("href") ?? string.Empty).Groups["code"].Value);
        var (ground, surface) = SplitGround(fields.GetValueOrDefault("terreno de juego"));

        var profile = new TeamProfile
        {
            OfficialName = officialName,
            Address = fields.GetValueOrDefault("domicilio"),
            Locality = fields.GetValueOrDefault("localidad"),
            Province = fields.GetValueOrDefault("provincia"),
            PostalCode = fields.GetValueOrDefault("c.p"),
            Website = fields.GetValueOrDefault("portal web"),
            Ground = ground,
            GroundSurface = surface,
            ShirtColor = fields.GetValueOrDefault("camiseta"),
            ShortsColor = fields.GetValueOrDefault("pantalon"),
            SocksColor = fields.GetValueOrDefault("medias"),
        };

        return (new OfficialTeamProfile(crest, profile), groundCode);
    }

    /// <summary>Dirección del campo; null si la página no tiene datos.</summary>
    public static GroundInfo? ParseGround(string html)
    {
        var document = Parser.ParseDocument(html);
        var fields = Fields(document);
        if (fields.Count == 0) return null;

        return new GroundInfo(
            fields.GetValueOrDefault("direccion"),
            fields.GetValueOrDefault("localidad"),
            fields.GetValueOrDefault("superficie de juego"));
    }

    /// <summary>"VALLOBIN(H.SINTETICA)" → ("Vallobin", "Hierba artificial").</summary>
    internal static (string? Ground, string? Surface) SplitGround(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return (null, null);

        var match = GroundWithSurfacePattern().Match(value);
        var name = match.Success ? match.Groups["name"].Value : value;
        var surface = match.Success ? match.Groups["surface"].Value.Trim() : null;

        return (TitleCase(name), surface is null ? null : Surfaces.GetValueOrDefault(surface) ?? TitleCase(surface));
    }

    /// <summary>Pares etiqueta normalizada → valor de todos los &lt;h5&gt;&lt;strong|b&gt;Etiqueta:&lt;/strong&gt; valor&lt;/h5&gt;; las etiquetas vacías se omiten.</summary>
    private static Dictionary<string, string> Fields(IDocument document)
    {
        var fields = new Dictionary<string, string>();
        foreach (var h5 in document.QuerySelectorAll("h5"))
        {
            var label = Label(h5);
            if (label.Length == 0) continue;

            var labelNode = h5.QuerySelector("strong, b")!;
            var value = Clean(h5.TextContent.Replace(labelNode.TextContent, string.Empty));
            if (value.Length > 0) fields.TryAdd(label, value);
        }
        return fields;
    }

    private static string Label(IElement h5)
    {
        var strong = h5.QuerySelector("strong, b");
        if (strong is null) return string.Empty;

        var text = Clean(strong.TextContent).TrimEnd(':').Trim();
        return RemoveDiacritics(text).ToLowerInvariant();
    }

    private static string? NullIfEmpty(string value) => value.Length == 0 ? null : value;

    private static string TitleCase(string value) =>
        CultureInfo.GetCultureInfo("es-ES").TextInfo.ToTitleCase(Clean(value).ToLowerInvariant());

    private static string RemoveDiacritics(string value)
    {
        var decomposed = value.Normalize(System.Text.NormalizationForm.FormD);
        return new string(decomposed.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray())
            .Normalize(System.Text.NormalizationForm.FormC);
    }
}
