using System.Globalization;
using TercerTiempo.Application.Trainings;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.Teams;
using TercerTiempo.Domain.Videos;

namespace TercerTiempo.Application.Videos;

// El vídeo se llama siempre como el partido: «Local vs Visitante dd-mm-aaaa» (con la parte si va en dos). El nombre que
// le dio quien lo subió no se guarda en ningún sitio.
internal static class MatchVideoNames
{
    private static readonly Dictionary<string, string> ExtensionByType = new(StringComparer.OrdinalIgnoreCase)
    {
        ["video/mp4"] = ".mp4",
        ["video/webm"] = ".webm",
        ["video/quicktime"] = ".mov",
        ["video/x-matroska"] = ".mkv",
    };

    public static async Task<string> TitleAsync(Match match, VideoPart part, ITeamStore teams, CancellationToken cancellationToken)
    {
        var names = (await teams.GetByIdsAsync([match.HomeTeamId, match.AwayTeamId], cancellationToken)).ToDictionary(t => t.Id, t => t.Name);
        return Title(names.GetValueOrDefault(match.HomeTeamId, "Local"), names.GetValueOrDefault(match.AwayTeamId, "Visitante"), match.KickoffAt, part);
    }

    public static string Title(string home, string away, DateTimeOffset kickoffAt, VideoPart part)
    {
        var date = TimeZoneInfo.ConvertTime(kickoffAt, TrainingClock.Zone).ToString("dd-MM-yyyy", CultureInfo.InvariantCulture);
        var tail = part switch
        {
            VideoPart.FirstHalf => $" {date} (1ª parte)",
            VideoPart.SecondHalf => $" {date} (2ª parte)",
            _ => $" {date}",
        };

        // La fecha y la parte no se recortan nunca: si los nombres son muy largos, se acortan ellos.
        var teams = $"{home.Trim()} vs {away.Trim()}";
        var room = MatchVideo.MaxTitleLength - tail.Length;
        if (teams.Length > room) teams = teams[..room].TrimEnd();
        return teams + tail;
    }

    // El mismo nombre como archivo, sin los caracteres que no admiten los sistemas de archivos y con la extensión real:
    // la del archivo subido si es válida y, si no, la de su tipo. Siempre acaba en extensión: al comprimir se cambia por
    // .mp4 y los nombres de equipo llevan puntos («C.D.»).
    public static string FileName(string title, string? uploadedFileName, string contentType)
    {
        var extension = Path.GetExtension(uploadedFileName ?? string.Empty).ToLowerInvariant();
        if (extension.Length is < 2 or > 10 || !extension[1..].All(char.IsAsciiLetterOrDigit))
            extension = ExtensionByType.GetValueOrDefault(contentType, ".mp4");

        var invalid = Path.GetInvalidFileNameChars().Concat(['<', '>', ':', '"', '/', '\\', '|', '?', '*']).ToHashSet();
        var name = new string(title.Select(c => invalid.Contains(c) || char.IsControl(c) ? '-' : c).ToArray()).Trim().TrimEnd('.');
        if (name.Length == 0) name = "partido";

        var room = MatchVideo.MaxFileNameLength - extension.Length;
        if (name.Length > room) name = name[..room].TrimEnd().TrimEnd('.');
        return name + extension;
    }
}
