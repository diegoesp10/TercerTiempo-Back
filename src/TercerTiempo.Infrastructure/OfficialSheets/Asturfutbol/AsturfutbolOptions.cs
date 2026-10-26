namespace TercerTiempo.Infrastructure.OfficialSheets.Asturfutbol;

public sealed class OfficialSheetOptions
{
    public const string SectionName = "OfficialSheets";

    public bool Enabled { get; set; } = true;

    // Descarga en segundo plano: cada cuántos minutos (desde la última pasada guardada en JobRuns; 0 = solo a mano) se
    // buscan actas que falten o toque refrescar, cuántas como máximo por pasada y cuánto se espera tras arrancar si
    // nunca se ha hecho ninguna.
    public int BackgroundScanMinutes { get; set; } = 60;

    public int BackgroundBatch { get; set; } = 60;

    public int BackgroundStartDelaySeconds { get; set; } = 20;

    // Cada semana (día y hora de la federación) se ponen en cola las actas de los últimos WeeklyQueueDays días de sus
    // ligas, para que el fin de semana se baje solo. 0 lo desactiva.
    public DayOfWeek WeeklyQueueDay { get; set; } = DayOfWeek.Monday;

    public TimeOnly WeeklyQueueAt { get; set; } = new(7, 0);

    public int WeeklyQueueDays { get; set; } = 7;

    public AsturfutbolOptions Asturfutbol { get; set; } = new();

    internal SourceLimits AsturfutbolLimits =>
        new(AsturfutbolOfficialSheetProvider.Name, Enabled, Asturfutbol.RequestDelayMs, Asturfutbol.MaxRequestsPerHour, Asturfutbol.MaxRequestsPerDay,
            Asturfutbol.MaxRequestsPerBurst, Asturfutbol.BurstRestMinutes);
}

public sealed class AsturfutbolOptions
{
    public string BaseUrl { get; set; } = "https://www.asturfutbol.es/pnfg/NPcd/";

    public string UserAgent { get; set; } = "TercerTiempo/1.0 (+https://github.com/diegoesp10/TercerTiempo-Back)";

    // La web corta la sesión en la petición 14 de una tanda seguida (visto el 22, el 24 y el 26-10-2026, a 10 s y a 30 s),
    // sin bloquear la IP: tandas de 12 como mucho (contando las redirecciones de la sesión) con un descanso entre ellas, y
    // cada tanda con una sesión nueva. Si aun así corta, se espera SessionCutPauseMinutes; solo un rechazo con sesión
    // recién abierta, un 403/429 o una página de bloqueo cuentan como bloqueo (BlockedPauseHours).
    public int RequestDelayMs { get; set; } = 30000;
    public int MaxRequestsPerHour { get; set; } = 20;
    public int MaxRequestsPerDay { get; set; } = 60;
    public int MaxRequestsPerBurst { get; set; } = 10;
    public int BurstRestMinutes { get; set; } = 40;
    public int SessionCutPauseMinutes { get; set; } = 60;
    public int CacheHours { get; set; } = 24;
    public int FailureRetryMinutes { get; set; } = 60;
    public int BlockedPauseHours { get; set; } = 24;

    public string CalendarCode { get; set; } = "1000120";
    public string TeamsCode { get; set; } = "1000119";

    public string TimeZone { get; set; } = "Europe/Madrid";

    public int MatchLength { get; set; } = 90;
}
