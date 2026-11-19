using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TercerTiempo.Application.Abstractions.Videos;
using TercerTiempo.Application.Videos.Analysis;
using TercerTiempo.Domain.OfficialSheets;
using TercerTiempo.Domain.Videos;

namespace TercerTiempo.Infrastructure.Videos;

// TercerTiempo.Analisis como proceso del sistema: «uv run --frozen tt-analisis analizar …» desde la carpeta del
// analizador (--frozen: nunca se resuelve ni se reinstala nada a mitad de un análisis; lo instala Start-Analyzer.ps1 con
// «uv sync --frozen»). El avance se lee de las líneas «##progreso N» de su salida estándar (--progreso); la salida de
// error se guarda entera en stderr.txt. Cada vídeo tiene su carpeta de salida (analysis/<vídeo>), que se vacía antes de
// cada pasada. Si el analizador pasa InactivityMinutes sin escribir nada se da por colgado y se mata con su árbol.
internal sealed class UvVideoAnalyzer(
    FileSystemVideoStorage storage,
    IOptions<VideoAnalysisOptions> options,
    IOptions<VideoOptions> videoOptions,
    ILogger<UvVideoAnalyzer> logger) : IVideoAnalyzer
{
    public const string ProgressPrefix = "##progreso ";
    public const string StatePrefix = "##estado ";
    public const string LogPrefix = "##hito ";
    public const string PauseCommand = "##pausa";
    public const string ResumeCommand = "##sigue";

    private static readonly IReadOnlyDictionary<string, string> Stages = new Dictionary<string, string>
    {
        ["modelos"] = "models",
        ["equipos"] = "teams",
        ["pasada"] = "tracking",
        ["jugadas"] = "plays",
        ["revision"] = "review",
        ["salidas"] = "outputs",
    };

    // Código de salida del analizador cuando se le exige GPU (--exigir-gpu) y torch no ve ninguna.
    public const int NoGpuExitCode = 3;

    // Versiones de acta-propuesta.json que esta app sabe leer.
    // La 2 trae a cada jugador identificado por su dorsal (con el acta oficial) en lugar de cada tracklet suelto.
    public static readonly IReadOnlySet<int> ProposalVersions = new HashSet<int> { 1, 2 };

    // Por debajo de esto el vídeo está dañado o cortado: no se da por bueno lo poco que se leyó.
    public const decimal MinReadFramesPct = 50m;

    public const string StderrFile = "stderr.txt";

    // Los dorsales del acta oficial que se le pasan al analizador (--plantillas).
    public const string RosterFile = "plantillas.json";

    private const int StderrTail = 4000;
    private const int MaxLoggedStderrChars = 1024 * 1024;
    private const int MaxCropBytes = 512 * 1024;

    public async Task<VideoAnalysisRun> AnalyzeAsync(VideoAnalysisRequest request, Func<VideoAnalysisUpdate, CancellationToken, Task> report, VideoAnalysisControl control, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var directory = AnalyzerDirectory(settings);
        var video = storage.PathOf(request.StorageKey);
        if (!File.Exists(video))
            throw new VideoAnalysisException("El archivo del vídeo no está en el almacenamiento.");

        var output = OutputFolder(request.VideoId);
        ResetFolder(output);
        if (request.Roster is { } roster)
            await File.WriteAllTextAsync(Path.Combine(output, RosterFile), RosterJson(roster), cancellationToken);

        var arguments = Arguments(video, output, request, settings);
        logger.LogDebug("Analizador: {Uv} {Arguments} (en {Directory})", UvPath(settings), string.Join(' ', arguments), directory);

        var stderrPath = Path.Combine(output, StderrFile);
        var result = await RunAsync(
            UvPath(settings), arguments, directory, stderrPath,
            TimeSpan.FromMinutes(Math.Max(1, settings.TimeoutMinutes)),
            settings.InactivityMinutes > 0 ? TimeSpan.FromMinutes(settings.InactivityMinutes) : null,
            report, cancellationToken,
            line => logger.LogDebug("Analizador: {Line}", line),
            control);

        if (result.ExitCode != 0)
            LogStderr(request.VideoId, result.ExitCode, stderrPath);
        if (result.ExitCode == NoGpuExitCode)
            throw new VideoAnalysisException("Este equipo no tiene una GPU que el analizador pueda usar (torch no ve CUDA): el vídeo no se analiza.");
        if (result.ExitCode != 0)
            throw new VideoAnalysisException($"El analizador devolvió {result.ExitCode}: {Trim(result.StderrTail)}");

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

    // uv run --frozen tt-analisis analizar <vídeo> --salida <carpeta> --equipos "<nuestro>,<rival>" [--campo LxA] --sin-clips
    internal static IReadOnlyList<string> Arguments(string video, string output, VideoAnalysisRequest request, VideoAnalysisOptions settings)
    {
        var args = new List<string>
        {
            "run", "--frozen", "tt-analisis", "analizar", video,
            "--salida", output,
            "--equipos", $"{TeamName(request.OurTeam)},{TeamName(request.RivalTeam)}",
            "--sin-clips", "--progreso",
        };
        if (request.Roster is not null)
            args.AddRange(["--plantillas", Path.Combine(output, RosterFile)]);
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

    // {"parte", "minutos_parte", "equipos": [{"lado", "nombre", "jugadores": [{"dorsal", "nombre", "titular", "entra", "sale", "goles"}]}],
    //  "resultado": {"local", "visitante"}, "goles": [{"lado", "minuto", "descuento"}]}; sin acta cerrada, sin resultado ni goles.
    internal static string RosterJson(VideoAnalysisRoster roster) => JsonSerializer.Serialize(new
    {
        parte = roster.Part switch { VideoPart.FirstHalf => "primera", VideoPart.SecondHalf => "segunda", _ => "partido" },
        minutos_parte = roster.HalfMinutes,
        equipos = roster.Teams.Select(t => new
        {
            lado = Side(t.Side),
            nombre = t.Name,
            jugadores = t.Players.Select(p => new { dorsal = p.ShirtNumber, nombre = p.Name, titular = p.Started, entra = p.MinuteIn, sale = p.MinuteOut, goles = p.Goals }),
        }),
        resultado = roster.Score is { } score ? new { local = score.Home, visitante = score.Away } : null,
        goles = (roster.Goals ?? []).Select(g => new { lado = Side(g.ScoringSide), minuto = g.Minute, descuento = g.ExtraMinute }),
    });

    private static string Side(MatchSide side) => side == MatchSide.Away ? "visitante" : "local";

    internal sealed record ProcessResult(int ExitCode, string StderrTail);

    // Lanza el proceso y lee lo que cuenta (avance, estado y registro) hasta que termina. La salida de error va entera a
    // `stderrPath`. Se mata con todo su árbol si se cancela, si pasa `timeout` (sin contar el tiempo en pausa) o si pasa
    // `inactivity` sin escribir ninguna línea (en pausa el analizador sigue dando señales); estos dos últimos, con un
    // VideoAnalysisException que lo explica. Las órdenes de `control` le llegan por su entrada estándar.
    internal static async Task<ProcessResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        string stderrPath,
        TimeSpan timeout,
        TimeSpan? inactivity,
        Func<VideoAnalysisUpdate, CancellationToken, Task> report,
        CancellationToken cancellationToken,
        Action<string>? log = null,
        VideoAnalysisControl? control = null)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var clock = new PausableDeadline(deadline, timeout);
        using var idle = new CancellationTokenSource();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token, idle.Token);

        using var process = Start(fileName, arguments, workingDirectory);
        ChildProcessJob.TryAssign(process);

        var input = new CommandWriter(process);
        void OnControl(bool paused)
        {
            clock.Pause(paused);
            input.Send(paused ? PauseCommand : ResumeCommand);
        }
        if (control is not null)
        {
            control.Changed += OnControl;
            if (control.Paused) OnControl(true);
        }

        var activity = new OutputActivity();
        using var stderr = new StderrSink(stderrPath);
        var errors = CollectAsync(process.StandardError, stderr, activity);
        var watchdog = WatchInactivityAsync(activity, inactivity, idle, stop.Token);
        var lastPercent = -1;

        try
        {
            while (await process.StandardOutput.ReadLineAsync(stop.Token) is { } line)
            {
                activity.Touch();
                if (ProgressPercent(line) is { } percent)
                {
                    if (percent == lastPercent) continue;
                    lastPercent = percent;
                    await report(new VideoAnalysisUpdate(Percent: percent), cancellationToken);
                }
                else if (LiveState(line) is { } live)
                {
                    await report(new VideoAnalysisUpdate(Live: live), cancellationToken);
                }
                else if (LogLine(line) is { } entry)
                {
                    log?.Invoke(entry);
                    await report(new VideoAnalysisUpdate(Log: entry), cancellationToken);
                }
                else if (line.Length > 0)
                {
                    log?.Invoke(line);
                }
            }

            await process.WaitForExitAsync(stop.Token);
        }
        catch
        {
            Kill(process);
            if (!cancellationToken.IsCancellationRequested && idle.IsCancellationRequested)
                throw new VideoAnalysisException(
                    $"El analizador lleva {Describe(inactivity!.Value)} sin dar señales (ni avance ni mensajes): se ha dado por colgado y se ha parado. {Trim(stderr.Tail)}".TrimEnd());
            if (!cancellationToken.IsCancellationRequested && deadline.IsCancellationRequested)
                throw new VideoAnalysisException($"El analizador superó el tiempo máximo de {Describe(timeout)}.");
            throw;
        }
        finally
        {
            if (control is not null) control.Changed -= OnControl;
            input.Close();
            await stop.CancelAsync();
            await watchdog;
            await errors;
        }

        return new ProcessResult(process.ExitCode, stderr.Tail);
    }

    // «##estado {json}» → el estado detallado (con los nombres de la app); null si la línea es otra cosa o no se entiende.
    internal static VideoAnalysisLive? LiveState(string line)
    {
        if (!line.StartsWith(StatePrefix, StringComparison.Ordinal)) return null;
        try
        {
            using var document = JsonDocument.Parse(line.AsMemory(StatePrefix.Length));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;

            var stage = root.TryGetProperty("etapa", out var s) && s.ValueKind == JsonValueKind.String && s.GetString() is { } name
                ? Stages.GetValueOrDefault(name, name)
                : "models";
            IReadOnlyList<string>? colors = root.TryGetProperty("colores", out var c) && c.ValueKind == JsonValueKind.Array
                ? c.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).Where(IsColor).Take(2).ToList()
                : null;

            return new VideoAnalysisLive(
                stage.Length > 20 ? stage[..20] : stage,
                root.TryGetProperty("pausado", out var p) && p.ValueKind == JsonValueKind.True,
                Int(root, "t_video"), Int(root, "tramo_inicio_s"), Int(root, "tramo_fin_s"),
                Int(root, "fotogramas"), Int(root, "fotogramas_total"),
                Int(root, "jugadores_en_imagen"), Int(root, "jugadores_seguidos"),
                Pct(root, "balon_pct"), Pct(root, "calibrado_pct"),
                Dec(root, "fps"), Dec(root, "velocidad"), Int(root, "resta_s"),
                Int(root, "muestras"), Int(root, "objetivo"),
                colors is { Count: > 0 } ? colors : null,
                Int(root, "toques"), Int(root, "eventos"), Int(root, "pausa_s"));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // «##hito texto» → el texto, en una línea y sin pasarse de largo.
    internal static string? LogLine(string line)
    {
        if (!line.StartsWith(LogPrefix, StringComparison.Ordinal)) return null;
        var text = line[LogPrefix.Length..].Trim();
        return text.Length == 0 ? null : text.Length > 300 ? text[..300] : text;
    }

    private static bool IsColor(string value) =>
        value.Length == 7 && value[0] == '#' && value.AsSpan(1).IndexOfAnyExcept("0123456789abcdefABCDEF") < 0;

    private static int? Int(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number)
            && double.IsFinite(number) && Math.Abs(number) < int.MaxValue
            ? (int)Math.Round(number)
            : null;

    private static decimal? Dec(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number)
            && double.IsFinite(number) && number is >= 0 and < 1_000_000
            ? Math.Round((decimal)number, 2)
            : null;

    private static decimal? Pct(JsonElement root, string name) => Dec(root, name) is { } value ? Math.Clamp(Math.Round(value, 1), 0, 100) : null;

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
        using (var proposalDocument = ParseJson(proposalFile))
        {
            EnsureKnownVersion(proposalDocument.RootElement);
            proposalJson = JsonSerializer.Serialize(proposalDocument.RootElement);
        }

        using var document = ParseJson(analysisFile);
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

        // Lo que se pudo leer del archivo (análisis anteriores no lo traen: null). Muy poco es un vídeo dañado.
        decimal? read = count is { } c && c.TryGetProperty("pct_leido", out var readValue) && readValue.ValueKind == JsonValueKind.Number
            ? Percent(readValue.GetDouble())
            : null;
        if (read < MinReadFramesPct)
            throw new VideoAnalysisException(string.Create(CultureInfo.GetCultureInfo("es-ES"),
                $"El analizador solo pudo leer el {read:0.#} % de los fotogramas del vídeo: el archivo parece dañado o cortado. Vuelve a subirlo."));

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
            crops,
            read);
    }

    // Un JSON que no se puede leer (o con NaN, que no es JSON) es un fallo del análisis, no algo pasajero.
    private static JsonDocument ParseJson(string file)
    {
        try
        {
            return JsonDocument.Parse(File.ReadAllText(file, Encoding.UTF8));
        }
        catch (JsonException ex)
        {
            throw new VideoAnalysisException($"El analizador dejó {Path.GetFileName(file)} en un formato que no se puede leer: {ex.Message}");
        }
    }

    private static void EnsureKnownVersion(JsonElement proposal)
    {
        var version = proposal.TryGetProperty("version", out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var number) ? number : (int?)null;
        if (version is { } known && ProposalVersions.Contains(known)) return;
        throw new VideoAnalysisException(
            $"El analizador dejó una propuesta de la versión {version?.ToString(CultureInfo.InvariantCulture) ?? "desconocida"} y esta app solo entiende la "
            + $"{string.Join(", ", ProposalVersions.Order())}: actualiza la app o el analizador para que vayan a la par.");
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
            RedirectStandardInput = true,
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

    private static async Task CollectAsync(StreamReader reader, StderrSink into, OutputActivity activity)
    {
        var buffer = new char[4096];
        int read;
        while ((read = await reader.ReadAsync(buffer, 0, buffer.Length)) > 0)
        {
            activity.Touch();
            into.Append(buffer, read);
        }
    }

    private static async Task WatchInactivityAsync(OutputActivity activity, TimeSpan? limit, CancellationTokenSource idle, CancellationToken cancellationToken)
    {
        if (limit is not { } max) return;
        var every = TimeSpan.FromSeconds(Math.Clamp(max.TotalSeconds / 4, 0.5, 30));
        try
        {
            while (true)
            {
                await Task.Delay(every, cancellationToken);
                if (activity.Quiet < max) continue;
                await idle.CancelAsync();
                return;
            }
        }
        catch (OperationCanceledException)
        {
            // El proceso terminó (o se paró por otra cosa).
        }
    }

    // La salida de error completa queda en el log cuando el analizador falla (y siempre en stderr.txt de su carpeta).
    private void LogStderr(Guid videoId, int exitCode, string path)
    {
        string text;
        try
        {
            text = File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : "";
        }
        catch (IOException ex)
        {
            text = $"(no se pudo leer {path}: {ex.Message})";
        }
        if (text.Length > MaxLoggedStderrChars) text = $"(…recortado: los últimos {MaxLoggedStderrChars} caracteres; entero en {path})\n" + text[^MaxLoggedStderrChars..];
        logger.LogError("Analizador del vídeo {Video}: terminó con el código {ExitCode}. Salida de error (también en {File}):\n{Stderr}",
            videoId, exitCode, path, text.Length > 0 ? text : "(vacía)");
    }

    private static string Describe(TimeSpan span) =>
        span.TotalMinutes >= 1
            ? $"{Math.Round(span.TotalMinutes):0} minutos"
            : $"{Math.Max(1, Math.Round(span.TotalSeconds)):0} segundos";

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

    // Las órdenes para el analizador, por su entrada estándar. Si el proceso ya ha terminado no pasa nada.
    private sealed class CommandWriter(Process process)
    {
        private readonly Lock _lock = new();
        private bool _closed;

        public void Send(string command)
        {
            lock (_lock)
            {
                if (_closed) return;
                try
                {
                    process.StandardInput.WriteLine(command);
                    process.StandardInput.Flush();
                }
                catch (Exception ex) when (ex is IOException or InvalidOperationException or ObjectDisposedException)
                {
                    _closed = true;
                }
            }
        }

        public void Close()
        {
            lock (_lock)
            {
                if (_closed) return;
                _closed = true;
                try
                {
                    process.StandardInput.Close();
                }
                catch (Exception ex) when (ex is IOException or InvalidOperationException or ObjectDisposedException)
                {
                    // Ya estaba cerrada.
                }
            }
        }
    }

    // El tiempo máximo del análisis, parado mientras está en pausa: una pausa larga no lo da por pasado de tiempo.
    private sealed class PausableDeadline
    {
        private readonly Lock _lock = new();
        private readonly CancellationTokenSource _source;
        private TimeSpan _remaining;
        private long _since = Environment.TickCount64;
        private bool _paused;

        public PausableDeadline(CancellationTokenSource source, TimeSpan timeout)
        {
            _source = source;
            _remaining = timeout;
            source.CancelAfter(timeout);
        }

        public void Pause(bool paused)
        {
            lock (_lock)
            {
                if (_paused == paused) return;
                var now = Environment.TickCount64;
                try
                {
                    if (paused)
                    {
                        _remaining -= TimeSpan.FromMilliseconds(now - _since);
                        _source.CancelAfter(Timeout.InfiniteTimeSpan);
                    }
                    else
                    {
                        _source.CancelAfter(_remaining > TimeSpan.Zero ? _remaining : TimeSpan.Zero);
                    }
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
                _since = now;
                _paused = paused;
            }
        }
    }

    // Cuándo escribió el proceso por última vez (en cualquiera de sus dos salidas).
    private sealed class OutputActivity
    {
        private long _last = Environment.TickCount64;

        public void Touch() => Interlocked.Exchange(ref _last, Environment.TickCount64);

        public TimeSpan Quiet => TimeSpan.FromMilliseconds(Environment.TickCount64 - Interlocked.Read(ref _last));
    }

    // La salida de error: entera al fichero y las últimas líneas en memoria, para el mensaje del fallo.
    private sealed class StderrSink : IDisposable
    {
        private readonly Lock _lock = new();
        private readonly StringBuilder _tail = new();
        private StreamWriter? _file;

        public StderrSink(string path)
        {
            try
            {
                _file = new StreamWriter(path, append: false, new UTF8Encoding(false)) { AutoFlush = true };
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _file = null;
            }
        }

        public string Tail
        {
            get
            {
                lock (_lock) return _tail.ToString();
            }
        }

        public void Append(char[] buffer, int count)
        {
            lock (_lock)
            {
                _tail.Append(buffer, 0, count);
                if (_tail.Length > StderrTail) _tail.Remove(0, _tail.Length - StderrTail);
                try
                {
                    _file?.Write(buffer, 0, count);
                }
                catch (IOException)
                {
                    // Sin disco para el fichero: se sigue con la cola en memoria.
                    _file = null;
                }
            }
        }

        public void Dispose()
        {
            lock (_lock)
            {
                _file?.Dispose();
                _file = null;
            }
        }
    }
}
