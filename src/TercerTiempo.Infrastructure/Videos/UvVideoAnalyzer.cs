using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TercerTiempo.Application.Abstractions.Videos;
using TercerTiempo.Application.Videos.Analysis;

namespace TercerTiempo.Infrastructure.Videos;

// TercerTiempo.Analisis como proceso del sistema: «uv run tt-analisis analizar …» desde la carpeta del analizador. El
// avance se lee de las líneas «##progreso N» de su salida estándar (--progreso); los errores, de la salida de error.
// Cada vídeo tiene su carpeta de salida (analysis/<vídeo>), que se vacía antes de cada pasada.
internal sealed class UvVideoAnalyzer(
    FileSystemVideoStorage storage,
    IOptions<VideoAnalysisOptions> options,
    IOptions<VideoOptions> videoOptions,
    ILogger<UvVideoAnalyzer> logger) : IVideoAnalyzer
{
    public const string ProgressPrefix = "##progreso ";

    // Código de salida del analizador cuando se le exige GPU (--exigir-gpu) y torch no ve ninguna.
    public const int NoGpuExitCode = 3;

    private const int StderrTail = 4000;
    private const int MaxCropBytes = 512 * 1024;

    public async Task<VideoAnalysisRun> AnalyzeAsync(VideoAnalysisRequest request, Func<int, CancellationToken, Task> progress, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var directory = AnalyzerDirectory(settings);
        var video = storage.PathOf(request.StorageKey);
        if (!File.Exists(video))
            throw new VideoAnalysisException("El archivo del vídeo no está en el almacenamiento.");

        var output = OutputFolder(request.VideoId);
        ResetFolder(output);

        var arguments = Arguments(video, output, request, settings);
        logger.LogDebug("Analizador: {Uv} {Arguments} (en {Directory})", UvPath(settings), string.Join(' ', arguments), directory);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(Math.Max(1, settings.TimeoutMinutes)));

        using var process = Start(UvPath(settings), arguments, directory);
        var stderr = new StringBuilder();
        var errors = CollectAsync(process.StandardError, stderr);
        var lastPercent = -1;

        try
        {
            while (await process.StandardOutput.ReadLineAsync(timeout.Token) is { } line)
            {
                if (ProgressPercent(line) is not { } percent)
                {
                    if (line.Length > 0) logger.LogDebug("Analizador: {Line}", line);
                    continue;
                }
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
                throw new VideoAnalysisException($"El analizador superó el tiempo máximo de {settings.TimeoutMinutes} minutos.");
            throw;
        }
        finally
        {
            await errors;
        }

        if (process.ExitCode == NoGpuExitCode)
            throw new VideoAnalysisException("Este equipo no tiene una GPU que el analizador pueda usar (torch no ve CUDA): el vídeo no se analiza.");
        if (process.ExitCode != 0)
            throw new VideoAnalysisException($"El analizador devolvió {process.ExitCode}: {Trim(stderr.ToString())}");

        return ReadRun(output, OutputKey(request.VideoId));
    }

    public Task DeleteOutputAsync(Guid videoId, CancellationToken cancellationToken)
    {
        try
        {
            var folder = OutputFolder(videoId);
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "No se pudo borrar la carpeta del análisis del vídeo {Video}", videoId);
        }
        return Task.CompletedTask;
    }

    // uv run tt-analisis analizar <vídeo> --salida <carpeta> --equipos "<nuestro>,<rival>" [--campo LxA] --sin-clips
    internal static IReadOnlyList<string> Arguments(string video, string output, VideoAnalysisRequest request, VideoAnalysisOptions settings)
    {
        var args = new List<string>
        {
            "run", "tt-analisis", "analizar", video,
            "--salida", output,
            "--equipos", $"{TeamName(request.OurTeam)},{TeamName(request.RivalTeam)}",
            "--sin-clips", "--progreso",
        };
        if (request is { PitchLengthM: { } length, PitchWidthM: { } width })
            args.AddRange(["--campo", string.Create(CultureInfo.InvariantCulture, $"{length:0.#}x{width:0.#}")]);
        if (!string.IsNullOrWhiteSpace(settings.Segment))
            args.AddRange(["--segmento", settings.Segment.Trim()]);
        if (!string.IsNullOrWhiteSpace(settings.Camera))
            args.AddRange(["--camara", settings.Camera.Trim()]);
        if (settings.RequireGpu)
            args.Add("--exigir-gpu");
        return args;
    }

    // «##progreso 42» → 42; null si la línea es otra cosa.
    internal static int? ProgressPercent(string line) =>
        line.StartsWith(ProgressPrefix, StringComparison.Ordinal)
        && int.TryParse(line.AsSpan(ProgressPrefix.Length).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var percent)
            ? Math.Clamp(percent, 0, 100)
            : null;

    // Lo que deja el analizador en su carpeta: la propuesta (acta-propuesta.json, que se guarda compacta), la calidad
    // y los pesos (analisis.json) y el recorte de cada jugador de la propuesta.
    internal static VideoAnalysisRun ReadRun(string folder, string outputKey)
    {
        var proposalFile = Path.Combine(folder, "acta-propuesta.json");
        var analysisFile = Path.Combine(folder, "analisis.json");
        if (!File.Exists(proposalFile) || !File.Exists(analysisFile))
            throw new VideoAnalysisException("El analizador terminó sin dejar la propuesta (acta-propuesta.json y analisis.json).");

        string proposalJson;
        using (var proposalDocument = JsonDocument.Parse(File.ReadAllText(proposalFile, Encoding.UTF8)))
            proposalJson = JsonSerializer.Serialize(proposalDocument.RootElement);

        using var document = JsonDocument.Parse(File.ReadAllText(analysisFile, Encoding.UTF8));
        var root = document.RootElement;
        var version = root.TryGetProperty("version", out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : "desconocida";

        var count = Object(root, "recuento");
        var videoInfo = Object(root, "video");
        var parameters = Object(root, "parametros");

        var weights = parameters is { } p && p.TryGetProperty("modelos", out var models) && models.ValueKind == JsonValueKind.Object
            ? string.Join(" · ", models.EnumerateObject().Select(m => $"{m.Name}: {m.Value}"))
            : "";
        if (weights.Length == 0) weights = "desconocidos";

        decimal? pitchLength = null, pitchWidth = null;
        if (parameters is { } pp && pp.TryGetProperty("campo_m", out var pitch) && pitch.ValueKind == JsonValueKind.Array && pitch.GetArrayLength() == 2)
        {
            pitchLength = Math.Round(pitch[0].GetDecimal(), 1);
            pitchWidth = Math.Round(pitch[1].GetDecimal(), 1);
        }

        var seconds = (int)Math.Round(Math.Max(0, Number(videoInfo, "tramo_fin_s") - Number(videoInfo, "tramo_inicio_s")));

        var crops = new Dictionary<int, byte[]>();
        var root2 = Path.GetFullPath(folder);
        foreach (var player in VideoProposal.Parse(proposalJson).Players)
        {
            if (player.CropFile is null) continue;
            var path = Path.GetFullPath(Path.Combine(root2, player.CropFile));
            if (!path.StartsWith(root2, StringComparison.OrdinalIgnoreCase) || !File.Exists(path)) continue;
            var info = new FileInfo(path);
            if (info.Length is > 0 and <= MaxCropBytes) crops[player.Tracklet] = File.ReadAllBytes(path);
        }

        return new VideoAnalysisRun(
            outputKey,
            version.Length > 40 ? version[..40] : version,
            weights.Length > 500 ? weights[..500] : weights,
            proposalJson,
            (int)Number(count, "fotogramas"),
            Percent(Number(count, "pct_con_balon")),
            Percent(Number(count, "pct_calibrados")),
            seconds,
            pitchLength,
            pitchWidth,
            crops);
    }

    private string OutputFolder(Guid videoId) => Path.Combine(OutputRoot(), videoId.ToString("N"));

    private static string OutputKey(Guid videoId) => $"analysis/{videoId:N}";

    private string OutputRoot() =>
        string.IsNullOrWhiteSpace(options.Value.OutputDirectory)
            ? Path.Combine(Path.GetFullPath(videoOptions.Value.Directory), "analysis")
            : Path.GetFullPath(Environment.ExpandEnvironmentVariables(options.Value.OutputDirectory));

    private static string AnalyzerDirectory(VideoAnalysisOptions settings)
    {
        if (string.IsNullOrWhiteSpace(settings.Directory))
            throw new VideoAnalysisException("No está configurada la carpeta del analizador de vídeo (Analysis:Directory).");

        var directory = Path.GetFullPath(Environment.ExpandEnvironmentVariables(settings.Directory));
        if (!File.Exists(Path.Combine(directory, "pyproject.toml")))
            throw new VideoAnalysisException($"No se encuentra el analizador de vídeo en {directory} (Analysis:Directory).");
        return directory;
    }

    private static string UvPath(VideoAnalysisOptions settings) => Environment.ExpandEnvironmentVariables(settings.UvPath);

    private static Process Start(string fileName, IReadOnlyList<string> arguments, string workingDirectory)
    {
        var info = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        // La salida de Python, en UTF-8 y sin retener (cada línea de avance llega en cuanto se escribe).
        info.Environment["PYTHONUTF8"] = "1";
        info.Environment["PYTHONIOENCODING"] = "utf-8";
        info.Environment["PYTHONUNBUFFERED"] = "1";

        try
        {
            return Process.Start(info) ?? throw new VideoAnalysisException($"No se pudo arrancar {fileName}.");
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            throw new VideoAnalysisException($"No se encuentra uv ({fileName}): instálalo o indica su ruta en Analysis:UvPath. {ex.Message}");
        }
    }

    private static void ResetFolder(string folder)
    {
        if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        Directory.CreateDirectory(folder);
    }

    private static async Task CollectAsync(StreamReader reader, StringBuilder into)
    {
        var buffer = new char[4096];
        int read;
        while ((read = await reader.ReadAsync(buffer, 0, buffer.Length)) > 0)
        {
            into.Append(buffer, 0, read);
            if (into.Length > StderrTail) into.Remove(0, into.Length - StderrTail);
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

    // Los nombres van separados por coma en --equipos.
    private static string TeamName(string name) => name.Replace(',', ' ').Trim();

    private static string Trim(string text)
    {
        var compact = text.ReplaceLineEndings(" ").Trim();
        return compact.Length > 600 ? compact[^600..] : compact;
    }

    private static JsonElement? Object(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Object ? value : null;

    private static double Number(JsonElement? element, string name) =>
        element is { } e && e.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetDouble() : 0;

    private static decimal Percent(double value) => Math.Round((decimal)Math.Clamp(value, 0, 100), 1);
}
