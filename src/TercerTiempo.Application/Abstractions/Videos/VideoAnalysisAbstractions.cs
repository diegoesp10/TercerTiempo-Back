namespace TercerTiempo.Application.Abstractions.Videos;

// Análisis del vídeo de un partido con TercerTiempo.Analisis (Python + GPU), como proceso externo del servicio de
// vídeo: cuando un vídeo termina de comprimirse entra en la cola y el analizador deja una propuesta de acta interna.
public sealed class VideoAnalysisOptions
{
    public const string SectionName = "Analysis";

    // Se admiten análisis: la API deja pedirlos y el compresor encola cada vídeo al terminar. Desactivado donde no hay
    // GPU en ningún sitio (Pilot, Production): los vídeos no entran en la cola y la app lo dice.
    public bool Enabled { get; set; }

    // Este proceso atiende la cola y lanza el analizador (hace falta uv, TercerTiempo.Analisis y la GPU). En Docker no:
    // allí se encola y lo analiza Start-Analyzer.ps1 desde Windows contra la misma base.
    public bool RunWorker { get; set; }

    // Ejecutable de uv; admite variables de entorno (%APPDATA%\Python\Python314\Scripts\uv.exe).
    public string UvPath { get; set; } = "uv";

    // Carpeta del analizador (la de su pyproject.toml): desde ella se lanza «uv run tt-analisis analizar».
    public string Directory { get; set; } = "";

    // Carpeta de las salidas (una subcarpeta por vídeo); vacía = analysis/ dentro de Videos:Directory.
    public string OutputDirectory { get; set; } = "";

    // Solo un tramo del vídeo (20:00-25:00), para probar sin esperar el partido entero.
    public string? Segment { get; set; }

    // movil: alguien graba con paneo y zoom; fija: trípode o cámara tipo Veo.
    public string Camera { get; set; } = "movil";

    // Sin GPU el analizador tardaría horas en CPU: se da por fallido con un mensaje claro.
    public bool RequireGpu { get; set; } = true;

    // Intentos por vídeo ante fallos pasajeros (la base, el disco) o un proceso que murió a medias; un fallo del propio
    // análisis (sin GPU, vídeo ilegible) no se reintenta.
    public int MaxAttempts { get; set; } = 2;

    public int PollSeconds { get; set; } = 15;

    public int TimeoutMinutes { get; set; } = 480;

    // Sin ninguna línea del analizador en este tiempo se da por colgado y se para (0 = sin vigilancia).
    public int InactivityMinutes { get; set; } = 15;

    // Un análisis «en marcha» cuyo latido lleva más de esto sin renovarse vuelve a la cola: su proceso murió.
    public int LeaseMinutes { get; set; } = 10;
}

// Los nombres van en el orden «el nuestro, el rival», pero el analizador no sabe cuál es cuál: separa dos equipaciones
// y es el cuerpo técnico quien dice cuál es la suya.
public sealed record VideoAnalysisRequest(
    Guid VideoId,
    string StorageKey,
    string OurTeam,
    string RivalTeam,
    decimal? PitchLengthM,
    decimal? PitchWidthM);

public sealed record VideoAnalysisRun(
    string OutputPath,
    string AnalyzerVersion,
    string Weights,
    string ProposalJson,
    int Frames,
    decimal BallFramesPct,
    decimal CalibratedFramesPct,
    int AnalyzedSeconds,
    decimal? PitchLengthM,
    decimal? PitchWidthM,
    IReadOnlyDictionary<int, byte[]> Crops,
    decimal? ReadFramesPct = null);

public interface IVideoAnalyzer
{
    // Lanza el analizador sobre el archivo del vídeo y devuelve lo que deja (acta-propuesta.json, analisis.json y los
    // recortes). El avance (en %) se comunica esperando a cada aviso. Lanza VideoAnalysisException con un mensaje
    // para el cuerpo técnico si no se puede analizar (sin uv, sin GPU, sin la carpeta del analizador...).
    Task<VideoAnalysisRun> AnalyzeAsync(VideoAnalysisRequest request, Func<int, CancellationToken, Task> progress, CancellationToken cancellationToken);

    // Borra la carpeta de salida de un vídeo (al eliminarlo).
    Task DeleteOutputAsync(Guid videoId, CancellationToken cancellationToken);
}

public sealed class VideoAnalysisException(string message) : Exception(message);

// Enlace entre quien encola o borra vídeos y el proceso que los analiza (los dos viven en el servicio de vídeo). Lo
// que se pide desde la API (volver a analizar) lo recoge el proceso al mirar la cola, cada Analysis:PollSeconds.
public interface IVideoAnalysisSignal
{
    void Wake();

    // El vídeo se ha eliminado: si se estaba analizando, se para.
    void Cancel(Guid videoId);

    Task WaitAsync(TimeSpan timeout, CancellationToken cancellationToken);

    IDisposable Track(Guid videoId, out CancellationToken cancellation);
}
