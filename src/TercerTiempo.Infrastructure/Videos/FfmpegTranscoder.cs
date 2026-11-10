using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TercerTiempo.Application.Abstractions.Videos;

namespace TercerTiempo.Infrastructure.Videos;

// ffmpeg y ffprobe como procesos del sistema (en Docker van en la imagen del servicio de vídeo). El avance se lee de
// -progress en la salida estándar; los errores, de la salida de error.
internal sealed class FfmpegTranscoder(FileSystemVideoStorage storage, IOptions<VideoProcessingOptions> options, ILogger<FfmpegTranscoder> logger) : IVideoTranscoder
{
    private const int StderrTail = 4000;
    private static readonly TimeSpan DurationTolerance = TimeSpan.FromSeconds(2);

    private bool? _available;

    public bool IsAvailable => _available ??= ToolsRespond();

    public async Task<VideoProbe> ProbeAsync(string storageKey, CancellationToken cancellationToken) =>
        await ProbeFileAsync(storage.PathOf(storageKey), cancellationToken);

    public Task<int> CleanUpAsync(CancellationToken cancellationToken) => Task.FromResult(storage.CleanProcessing());

    public async Task<TranscodeResult> TranscodeAsync(
        string storageKey, TranscodeProfile profile, Func<int, CancellationToken, Task> progress, CancellationToken cancellationToken)
    {
        var source = storage.PathOf(storageKey);
        var input = await ProbeFileAsync(source, cancellationToken);
        var work = storage.NewProcessingPath(".mp4");

        try
        {
            await RunFfmpegAsync(Arguments(source, work, profile), input.DurationSeconds, progress, cancellationToken);

            var output = await ProbeFileAsync(work, cancellationToken);
            if (Math.Abs(output.DurationSeconds - input.DurationSeconds) > DurationTolerance.TotalSeconds)
                throw new VideoTranscodeException($"El comprimido dura {Describe(output.DurationSeconds)} y el original {Describe(input.DurationSeconds)}: no se da por bueno.");

            var size = new FileInfo(work).Length;
            var key = storage.Publish(work, ".mp4");
            return new TranscodeResult(key, size, output.DurationSeconds, output.Height);
        }
        catch
        {
            TryDelete(work);
            throw;
        }
    }

    // Vídeo H.264 (perfil high, yuv420p: lo reproduce cualquier navegador y móvil), un fotograma clave cada 120 para
    // saltar fino en la línea de tiempo, audio AAC estéreo y el índice al principio (faststart) para empezar a verlo
    // sin descargarlo entero. Solo el primer vídeo y el primer audio: las pistas de datos de las cámaras no caben en MP4.
    internal static IReadOnlyList<string> Arguments(string source, string output, TranscodeProfile profile)
    {
        var args = new List<string>
        {
            "-y", "-nostdin", "-hide_banner", "-loglevel", "error", "-nostats", "-progress", "pipe:1",
            "-i", source,
            "-map", "0:v:0", "-map", "0:a:0?", "-sn", "-dn",
        };
        if (profile.MaxHeight is { } height) args.AddRange(["-vf", $"scale=-2:{height}"]);
        args.AddRange(
        [
            "-c:v", "libx264", "-preset", profile.Preset, "-crf", profile.Crf.ToString(CultureInfo.InvariantCulture),
            "-profile:v", "high", "-pix_fmt", "yuv420p", "-g", "120",
            "-c:a", "aac", "-b:a", $"{profile.AudioKbps}k", "-ac", "2",
            "-movflags", "+faststart", "-f", "mp4", output,
        ]);
        return args;
    }

    // Lo que imprime -progress: out_time_us (microsegundos) en las versiones nuevas, out_time_ms en las anteriores
    // (también microsegundos, pese al nombre). null si la línea no dice por dónde va.
    internal static double? ProgressSeconds(string line)
    {
        var separator = line.IndexOf('=');
        if (separator <= 0) return null;
        var key = line.AsSpan(0, separator).Trim();
        if (!key.SequenceEqual("out_time_us") && !key.SequenceEqual("out_time_ms")) return null;
        return long.TryParse(line.AsSpan(separator + 1).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var micro) && micro >= 0
            ? micro / 1_000_000d
            : null;
    }

    internal static VideoProbe ParseProbe(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        string? videoCodec = null, audioCodec = null;
        int width = 0, height = 0;
        double streamDuration = 0;
        if (root.TryGetProperty("streams", out var streams))
        {
            foreach (var stream in streams.EnumerateArray())
            {
                var type = Text(stream, "codec_type");
                if (type == "video" && videoCodec is null)
                {
                    videoCodec = Text(stream, "codec_name");
                    width = Number(stream, "width");
                    height = Number(stream, "height");
                    streamDuration = Decimal(stream, "duration");
                }
                else if (type == "audio" && audioCodec is null)
                {
                    audioCodec = Text(stream, "codec_name");
                }
            }
        }

        var format = root.TryGetProperty("format", out var f) ? f : default;
        var duration = format.ValueKind == JsonValueKind.Object ? Decimal(format, "duration") : 0;
        if (duration <= 0) duration = streamDuration;
        var bitrate = format.ValueKind == JsonValueKind.Object ? (long)Decimal(format, "bit_rate") : 0;
        var size = format.ValueKind == JsonValueKind.Object ? (long)Decimal(format, "size") : 0;
        if (bitrate <= 0 && duration > 0 && size > 0) bitrate = (long)(size * 8 / duration);
        var container = format.ValueKind == JsonValueKind.Object ? Text(format, "format_name") ?? "" : "";

        if (videoCodec is null || duration <= 0 || height <= 0)
            throw new VideoTranscodeException("ffprobe no reconoce el archivo como un vídeo.");

        return new VideoProbe(duration, width, height, bitrate, videoCodec, audioCodec, container);
    }

    private async Task<VideoProbe> ProbeFileAsync(string path, CancellationToken cancellationToken)
    {
        var (exitCode, stdout, stderr) = await RunAsync(options.Value.FfprobePath,
            ["-v", "error", "-print_format", "json", "-show_format", "-show_streams", path], TimeSpan.FromMinutes(2), cancellationToken);
        if (exitCode != 0)
            throw new VideoTranscodeException($"ffprobe devolvió {exitCode}: {Trim(stderr)}");
        return ParseProbe(stdout);
    }

    private async Task RunFfmpegAsync(IReadOnlyList<string> arguments, double durationSeconds, Func<int, CancellationToken, Task> progress, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(Math.Max(1, options.Value.TimeoutMinutes)));

        using var process = Start(options.Value.FfmpegPath, arguments);
        var stderr = new StringBuilder();
        var errors = CollectAsync(process.StandardError, stderr);
        var lastPercent = -1;

        try
        {
            while (await process.StandardOutput.ReadLineAsync(timeout.Token) is { } line)
            {
                if (ProgressSeconds(line) is not { } seconds || durationSeconds <= 0) continue;
                var percent = Math.Clamp((int)Math.Floor(seconds / durationSeconds * 100), 0, 99);
                if (percent == lastPercent) continue;
                lastPercent = percent;
                await progress(percent, cancellationToken);
            }

            await process.WaitForExitAsync(timeout.Token);
        }
        catch
        {
            Kill(process);
            if (!cancellationToken.IsCancellationRequested && timeout.IsCancellationRequested)
                throw new VideoTranscodeException($"ffmpeg superó el tiempo máximo de {options.Value.TimeoutMinutes} minutos.");
            throw;
        }
        finally
        {
            await errors;
        }

        if (process.ExitCode != 0)
            throw new VideoTranscodeException($"ffmpeg devolvió {process.ExitCode}: {Trim(stderr.ToString())}");
    }

    private async Task<(int ExitCode, string Stdout, string Stderr)> RunAsync(string fileName, IReadOnlyList<string> arguments, TimeSpan limit, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(limit);
        using var process = Start(fileName, arguments);
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        var reads = Task.WhenAll(CollectAsync(process.StandardOutput, stdout, int.MaxValue), CollectAsync(process.StandardError, stderr));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch
        {
            Kill(process);
            throw;
        }
        await reads;
        return (process.ExitCode, stdout.ToString(), stderr.ToString());
    }

    private static Process Start(string fileName, IReadOnlyList<string> arguments)
    {
        var info = new ProcessStartInfo(fileName)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        return Process.Start(info) ?? throw new VideoTranscodeException($"No se pudo arrancar {fileName}.");
    }

    private static async Task CollectAsync(StreamReader reader, StringBuilder into, int keep = StderrTail)
    {
        var buffer = new char[4096];
        int read;
        while ((read = await reader.ReadAsync(buffer, 0, buffer.Length)) > 0)
        {
            into.Append(buffer, 0, read);
            if (into.Length > keep) into.Remove(0, into.Length - keep);
        }
    }

    private static void Kill(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // Ya había terminado.
        }
    }

    private bool ToolsRespond()
    {
        foreach (var tool in new[] { options.Value.FfmpegPath, options.Value.FfprobePath })
        {
            try
            {
                using var process = Start(tool, ["-version"]);
                process.StandardOutput.ReadToEnd();
                process.StandardError.ReadToEnd();
                if (!process.WaitForExit(TimeSpan.FromSeconds(15)) || process.ExitCode != 0)
                {
                    logger.LogWarning("{Tool} no responde: los vídeos se quedarán sin comprimir", tool);
                    return false;
                }
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or VideoTranscodeException or InvalidOperationException)
            {
                logger.LogWarning("{Tool} no está instalado o no se encuentra ({Error}): los vídeos se quedarán sin comprimir", tool, ex.Message);
                return false;
            }
        }
        return true;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException)
        {
            // Lo limpia el servicio al arrancar.
        }
    }

    private static string Trim(string text)
    {
        var compact = text.ReplaceLineEndings(" ").Trim();
        return compact.Length > 500 ? compact[^500..] : compact;
    }

    private static string Describe(double seconds) => TimeSpan.FromSeconds(seconds).ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture);

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static int Number(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) ? number : 0;

    // ffprobe escribe los números como texto ("5781.700000").
    private static double Decimal(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value)) return 0;
        return value.ValueKind switch
        {
            JsonValueKind.Number => value.GetDouble(),
            JsonValueKind.String when double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => 0,
        };
    }
}
