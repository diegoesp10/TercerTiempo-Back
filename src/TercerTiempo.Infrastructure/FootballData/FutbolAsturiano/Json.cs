using System.Globalization;
using System.Net;
using System.Text.Json;

namespace TercerTiempo.Infrastructure.FootballData.FutbolAsturiano;

internal static class Json
{
    public static string? GetString(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(property, out var value))
            return null;

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null,
        };
    }

    public static int? GetInt(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(property, out var value))
            return null;

        return ToInt(value);
    }

    public static int? ToInt(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Number when value.TryGetInt32(out var number) => number,
        JsonValueKind.Number when value.TryGetDouble(out var real) => (int)real,
        JsonValueKind.String when int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
        _ => null,
    };

    public static List<int> GetIntArray(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.Array)
            return [];

        return value.EnumerateArray().Select(ToInt).Where(v => v is > 0).Select(v => v!.Value).ToList();
    }

    public static string GetRenderedTitle(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty("title", out var title))
            return string.Empty;

        var raw = title.ValueKind == JsonValueKind.Object ? GetString(title, "rendered") : title.GetString();
        return WebUtility.HtmlDecode(raw ?? string.Empty).Trim();
    }

    public static DateTimeOffset? GetUtcDate(JsonElement element, string property)
    {
        var raw = GetString(element, property);
        if (string.IsNullOrWhiteSpace(raw)) return null;

        return DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var date)
            ? new DateTimeOffset(DateTime.SpecifyKind(date, DateTimeKind.Utc))
            : null;
    }

    public static DateTime? GetLocalDateTime(JsonElement element, string property)
    {
        var raw = GetString(element, property);
        if (string.IsNullOrWhiteSpace(raw)) return null;

        return DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? DateTime.SpecifyKind(date, DateTimeKind.Unspecified)
            : null;
    }

    public static int? GetGoals(JsonElement eventElement, int teamId)
    {
        if (eventElement.ValueKind != JsonValueKind.Object || !eventElement.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Object)
            return null;

        if (!results.TryGetProperty(teamId.ToString(CultureInfo.InvariantCulture), out var teamResult) || teamResult.ValueKind != JsonValueKind.Object)
            return null;

        if (!teamResult.TryGetProperty("goals", out var goals))
            return null;

        return ToInt(goals);
    }

    public static bool HasOutcome(JsonElement eventElement, int teamId)
    {
        if (eventElement.ValueKind != JsonValueKind.Object || !eventElement.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Object)
            return false;

        if (!results.TryGetProperty(teamId.ToString(CultureInfo.InvariantCulture), out var teamResult) || teamResult.ValueKind != JsonValueKind.Object)
            return false;

        if (!teamResult.TryGetProperty("outcome", out var outcome))
            return false;

        return outcome.ValueKind switch
        {
            JsonValueKind.String => !string.IsNullOrWhiteSpace(outcome.GetString()),
            JsonValueKind.Array => outcome.GetArrayLength() > 0,
            _ => false,
        };
    }

    public static string? GetCrestUrl(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty("_embedded", out var embedded)
            || embedded.ValueKind != JsonValueKind.Object
            || !embedded.TryGetProperty("wp:featuredmedia", out var media)
            || media.ValueKind != JsonValueKind.Array)
            return null;

        foreach (var item in media.EnumerateArray())
        {
            var url = GetString(item, "source_url");
            if (!string.IsNullOrWhiteSpace(url)) return url;
        }

        return null;
    }
}
