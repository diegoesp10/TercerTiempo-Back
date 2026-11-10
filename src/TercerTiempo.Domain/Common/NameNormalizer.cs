using System.Globalization;
using System.Text;

namespace TercerTiempo.Domain.Common;

public static class NameNormalizer
{
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        var decomposed = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        var lastWasSeparator = false;

        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;

            if (char.IsWhiteSpace(ch) || ch is '-' or '.' or '_')
            {
                if (!lastWasSeparator)
                {
                    builder.Append(' ');
                    lastWasSeparator = true;
                }
                continue;
            }

            builder.Append(ch);
            lastWasSeparator = false;
        }

        return builder.ToString().Trim();
    }
}
