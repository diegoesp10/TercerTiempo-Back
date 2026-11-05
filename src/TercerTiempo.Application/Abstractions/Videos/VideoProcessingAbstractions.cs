namespace TercerTiempo.Application.Abstractions.Videos;

// Compresión en segundo plano de los vídeos subidos: H.264 sin bajar de MaxHeight (solo se reduce lo que venga por
// encima), con la calidad fija de Crf, en un proceso aparte de la API (el servicio de vídeo).
public sealed class VideoProcessingOptions
{
    public const string SectionName = "VideoProcessing";

    public bool Enabled { get; set; } = true;

    // Calidad constante de x264: 22 es visualmente igual que el original para fútbol grabado con móvil o cámara.
    public int Crf { get; set; } = 22;

    public string Preset { get; set; } = "medium";

    public int MaxHeight { get; set; } = 1080;

    public int AudioKbps { get; set; } = 128;

    // Un archivo ya en H.264 dentro de un MP4, a MaxHeight o menos y por debajo de este caudal, se deja tal cual.
    public int SkipBelowKbps { get; set; } = 4500;

    public int MaxAttempts { get; set; } = 2;

    public int PollSeconds { get; set; } = 30;

    public int TimeoutMinutes { get; set; } = 360;

    public string FfmpegPath { get; set; } = "ffmpeg";

    public string FfprobePath { get; set; } = "ffprobe";
}

public sealed record VideoProbe(
    double DurationSeconds,
    int Width,
    int Height,
    long BitrateBps,
    string? VideoCodec,
    string? AudioCodec,
    string Container);

// MaxHeight null: se conserva la resolución del original.
public sealed record TranscodeProfile(int Crf, string Preset, int? MaxHeight, int AudioKbps);

public sealed record TranscodeResult(string StorageKey, long SizeBytes, double DurationSeconds, int Height);

public interface IVideoTranscoder
{
    bool IsAvailable { get; }

    Task<VideoProbe> ProbeAsync(string storageKey, CancellationToken cancellationToken);

    // Comprime el archivo de storageKey y deja el resultado publicado con una clave nueva, comprobada su duración. El
    // avance (en %) se comunica esperando a cada aviso, así quien escucha puede guardarlo sin carreras.
    Task<TranscodeResult> TranscodeAsync(
        string storageKey, TranscodeProfile profile, Func<int, CancellationToken, Task> progress, CancellationToken cancellationToken);

    // Borra los restos de compresiones que no acabaron (el servicio se paró a medias); devuelve cuántos.
    Task<int> CleanUpAsync(CancellationToken cancellationToken);
}

public sealed class VideoTranscodeException(string message) : Exception(message);

// Enlace entre quien sube o borra vídeos y el proceso que los comprime (los dos viven en el servicio de vídeo).
public interface IVideoProcessingSignal
{
    // Hay un vídeo nuevo en la cola.
    void Wake();

    // El vídeo se ha eliminado: si se estaba comprimiendo, se para.
    void Cancel(Guid videoId);

    // Espera a que haya trabajo o a que pase el plazo.
    Task WaitAsync(TimeSpan timeout, CancellationToken cancellationToken);

    // Token que se cancela si se pide Cancel(videoId) mientras se comprime ese vídeo.
    IDisposable Track(Guid videoId, out CancellationToken cancellation);
}

// Historial de compresiones en /jobs (una ejecución por vídeo).
public interface IVideoJobLog
{
    Task<Guid?> StartedAsync(string label, CancellationToken cancellationToken);

    Task FinishedAsync(Guid? runId, bool succeeded, string summary, string? error, CancellationToken cancellationToken);
}
