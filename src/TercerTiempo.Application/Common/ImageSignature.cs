using System.Text;

namespace TercerTiempo.Application.Common;

// El tipo que manda el navegador (o la web de origen) se puede inventar: se comprueba que los primeros bytes sean de
// verdad de ese formato antes de guardar la imagen y servirla con ese tipo.
public static class ImageSignature
{
    public static bool Matches(ReadOnlySpan<byte> data, string contentType) => contentType.ToLowerInvariant() switch
    {
        "image/png" => data.StartsWith((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]),
        "image/jpeg" => data.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xD8, 0xFF]),
        "image/gif" => data.StartsWith("GIF87a"u8) || data.StartsWith("GIF89a"u8),
        "image/webp" => data.Length >= 12 && data[..4].SequenceEqual("RIFF"u8) && data.Slice(8, 4).SequenceEqual("WEBP"u8),
        "image/svg+xml" => LooksLikeSvg(data),
        _ => false,
    };

    private static bool LooksLikeSvg(ReadOnlySpan<byte> data)
    {
        var head = Encoding.UTF8.GetString(data[..Math.Min(data.Length, 1024)]).TrimStart('﻿', ' ', '\t', '\r', '\n');
        return head.StartsWith("<svg", StringComparison.OrdinalIgnoreCase)
            || (head.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase) && head.Contains("<svg", StringComparison.OrdinalIgnoreCase))
            || (head.StartsWith("<!--", StringComparison.Ordinal) && head.Contains("<svg", StringComparison.OrdinalIgnoreCase));
    }
}
