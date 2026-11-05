using TercerTiempo.Application.Abstractions.Videos;

namespace TercerTiempo.Application.Videos.Processing;

public abstract record TranscodeDecision
{
    public sealed record Keep(string Reason) : TranscodeDecision;

    public sealed record Transcode(TranscodeProfile Profile) : TranscodeDecision;
}

// Qué hacer con un vídeo recién subido: dejarlo como está si ya viene ligero y en un formato que el navegador
// reproduce, o recomprimirlo (y reducirlo a MaxHeight solo si viene más grande).
public static class TranscodePlan
{
    public static TranscodeDecision Decide(VideoProbe probe, VideoProcessingOptions options)
    {
        var tooTall = probe.Height > options.MaxHeight;
        var light = probe.BitrateBps > 0 && probe.BitrateBps <= options.SkipBelowKbps * 1000L;
        var h264 = string.Equals(probe.VideoCodec, "h264", StringComparison.OrdinalIgnoreCase);
        var mp4 = probe.Container.Split(',').Any(c => c.Trim().Equals("mp4", StringComparison.OrdinalIgnoreCase));
        var aac = probe.AudioCodec is null || string.Equals(probe.AudioCodec, "aac", StringComparison.OrdinalIgnoreCase);

        if (!tooTall && light && h264 && mp4 && aac)
            return new TranscodeDecision.Keep($"ya viene en H.264 a {probe.BitrateBps / 1000} kbps");

        return new TranscodeDecision.Transcode(new TranscodeProfile(options.Crf, options.Preset, tooTall ? options.MaxHeight : null, options.AudioKbps));
    }
}
