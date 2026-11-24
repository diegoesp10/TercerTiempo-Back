using AngleSharp.Html.Parser;

namespace TercerTiempo.Infrastructure.FootballData.FutbolAsturiano;

// La crónica de un partido (el contenido de la entrada en WordPress) en texto: un párrafo por línea, sin imágenes ni
// etiquetas.
internal static class ReportText
{
    private const string Blocks = "p, h1, h2, h3, h4, h5, h6, li, blockquote";

    public static string FromHtml(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return string.Empty;

        var document = new HtmlParser().ParseDocument($"<!DOCTYPE html><html><body>{html}</body></html>");
        var body = document.Body;
        if (body is null) return string.Empty;

        var lines = new List<string>();
        foreach (var block in body.QuerySelectorAll(Blocks))
        {
            // Un bloque que lleva otros dentro (una cita con párrafos) se lee por sus párrafos.
            if (block.QuerySelector(Blocks) is not null) continue;
            var text = Clean(block.TextContent);
            if (text.Length > 0) lines.Add(text);
        }

        if (lines.Count == 0)
        {
            var all = Clean(body.TextContent);
            if (all.Length > 0) lines.Add(all);
        }
        return string.Join('\n', lines);
    }

    private static string Clean(string text) =>
        string.Join(' ', text.Replace(' ', ' ').Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
