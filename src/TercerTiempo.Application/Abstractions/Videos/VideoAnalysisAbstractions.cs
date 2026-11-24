using System.ComponentModel;
using TercerTiempo.Domain.OfficialSheets;
using TercerTiempo.Domain.Videos;

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

// Los nombres van en el orden «el nuestro, el rival». Con el acta oficial (Roster) el analizador lee los dorsales y
// sabe qué equipación es de cada lado; sin ella separa dos equipaciones y es el cuerpo técnico quien dice cuál es la suya.
public sealed record VideoAnalysisRequest(
    Guid VideoId,
    string StorageKey,
    string OurTeam,
    string RivalTeam,
    decimal? PitchLengthM,
    decimal? PitchWidthM,
    VideoAnalysisRoster? Roster = null);

// Los que jugaron según el acta oficial, por lado, con sus minutos: de qué parte es el vídeo y cuánto dura cada parte
// para pasar los minutos del acta al tiempo del vídeo. Con el acta cerrada, también el resultado y el minuto de cada gol:
// el vídeo no puede dar más goles que el acta.
public sealed record VideoAnalysisRoster(
    VideoPart Part,
    int HalfMinutes,
    IReadOnlyList<VideoAnalysisRosterTeam> Teams,
    VideoAnalysisScore? Score = null,
    IReadOnlyList<VideoAnalysisGoal>? Goals = null);

public sealed record VideoAnalysisRosterTeam(MatchSide Side, string Name, IReadOnlyList<VideoAnalysisRosterPlayer> Players);

public sealed record VideoAnalysisRosterPlayer(int ShirtNumber, string Name, bool Started, int? MinuteIn, int? MinuteOut, int Goals = 0);

public sealed record VideoAnalysisScore(int Home, int Away);

// ScoringSide es el lado al que sube el gol (el contrario del que lo marca en propia puerta).
public sealed record VideoAnalysisGoal(MatchSide ScoringSide, int Minute, int ExtraMinute = 0);

public sealed record VideoAnalysisRun(
    string OutputPath,
    string AnalyzerName,
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

// Lo que va contando el analizador mientras trabaja: el avance en %, su estado detallado o una línea para el registro.
public sealed record VideoAnalysisUpdate(int? Percent = null, VideoAnalysisLive? Live = null, string? Log = null);

// El estado detallado que cuenta el analizador cada pocos segundos (lo que se enseña mientras analiza). Todo es opcional:
// cada etapa cuenta lo suyo.
public sealed record VideoAnalysisLive(
    [property: Description("models (cargando los detectores), teams (separando las equipaciones), tracking (siguiendo jugadores y balón fotograma a fotograma), plays (posesión y jugadas), review (revisión con el modelo de lenguaje) o outputs (escribiendo la propuesta).")]
    string Stage,
    [property: Description("El proceso está parado de verdad (ha llegado al punto de control tras pedir la pausa).")]
    bool Paused,
    [property: Description("Segundo del vídeo por el que va.")]
    int? VideoSecond = null,
    int? FromSecond = null,
    int? ToSecond = null,
    int? Frames = null,
    int? FramesTotal = null,
    [property: Description("Jugadores de campo en el fotograma actual.")]
    int? PlayersOnScreen = null,
    [property: Description("Jugadores seguidos hasta ahora (un jugador que se pierde de vista y vuelve puede contar dos veces).")]
    int? PlayersTracked = null,
    [property: Description("% de los fotogramas analizados con el balón a la vista.")]
    decimal? BallSeenPct = null,
    [property: Description("% de los fotogramas analizados con el campo calibrado.")]
    decimal? CalibratedPct = null,
    [property: Description("Fotogramas analizados por segundo.")]
    decimal? Fps = null,
    [property: Description("Segundos de vídeo analizados por segundo de reloj (1 = tiempo real).")]
    decimal? Speed = null,
    [property: Description("Lo que calcula el analizador que le queda de esta etapa, en segundos, sin contar las pausas.")]
    int? RemainingSeconds = null,
    int? TeamSamples = null,
    int? TeamSamplesTarget = null,
    [property: Description("Colores medios de las dos equipaciones (#rrggbb), en cuanto se separan.")]
    IReadOnlyList<string>? Colors = null,
    int? Touches = null,
    int? Events = null,
    [property: Description("Segundos que ha estado en pausa en total.")]
    int? PausedSeconds = null);

// Pausar y reanudar el análisis en marcha: el procesador lo cambia cuando lo pide la app y el analizador se lo dice al
// proceso.
public sealed class VideoAnalysisControl
{
    private volatile bool _paused;

    public bool Paused => _paused;

    public event Action<bool>? Changed;

    public void Set(bool paused)
    {
        if (_paused == paused) return;
        _paused = paused;
        Changed?.Invoke(paused);
    }
}

public interface IVideoAnalyzer
{
    // Lanza el analizador sobre el archivo del vídeo y devuelve lo que deja (acta-propuesta.json, analisis.json y los
    // recortes). Lo que va contando (avance, estado, registro) se comunica esperando a cada aviso, y obedece a `control`
    // (pausa y reanudación) sin perder lo hecho. Lanza VideoAnalysisException con un mensaje para el cuerpo técnico si no
    // se puede analizar (sin uv, sin GPU, sin la carpeta del analizador...).
    Task<VideoAnalysisRun> AnalyzeAsync(VideoAnalysisRequest request, Func<VideoAnalysisUpdate, CancellationToken, Task> report, VideoAnalysisControl control, CancellationToken cancellationToken);

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
