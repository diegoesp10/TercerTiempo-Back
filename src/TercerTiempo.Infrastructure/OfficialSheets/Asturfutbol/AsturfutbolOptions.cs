namespace TercerTiempo.Infrastructure.OfficialSheets.Asturfutbol;

public sealed class OfficialSheetOptions
{
    public const string SectionName = "OfficialSheets";

    public bool Enabled { get; set; } = true;

    // Descarga en segundo plano: cada cuántos minutos se buscan actas que falten o toque refrescar, cuántas como
    // máximo por pasada y cuánto se espera tras arrancar antes de la primera.
    public int BackgroundScanMinutes { get; set; } = 60;

    public int BackgroundBatch { get; set; } = 60;

    public int BackgroundStartDelaySeconds { get; set; } = 20;

    public AsturfutbolOptions Asturfutbol { get; set; } = new();

    public global::TercerTiempo.Infrastructure.OfficialSheets.BeSoccer.BeSoccerOptions BeSoccer { get; set; } = new();

    internal SourceLimits AsturfutbolLimits =>
        new(AsturfutbolOfficialSheetProvider.Name, Enabled, Asturfutbol.RequestDelayMs, Asturfutbol.MaxRequestsPerHour, Asturfutbol.MaxRequestsPerDay);

    internal SourceLimits BeSoccerLimits =>
        new(global::TercerTiempo.Infrastructure.OfficialSheets.BeSoccer.BeSoccerDirectory.Name, Enabled && BeSoccer.Enabled,
            BeSoccer.RequestDelayMs, BeSoccer.MaxRequestsPerHour, BeSoccer.MaxRequestsPerDay);
}

public sealed class AsturfutbolOptions
{
    public string BaseUrl { get; set; } = "https://www.asturfutbol.es/pnfg/NPcd/";

    public string UserAgent { get; set; } = "TercerTiempo/1.0 (+https://github.com/diegoesp10/TercerTiempo-Back)";

    // La web bloquea la IP tras unas 15 peticiones seguidas aunque vayan espaciadas: mejor pocas y lentas.
    public int RequestDelayMs { get; set; } = 30000;
    public int MaxRequestsPerHour { get; set; } = 20;
    public int MaxRequestsPerDay { get; set; } = 60;
    public int CacheHours { get; set; } = 24;
    public int FailureRetryMinutes { get; set; } = 60;
    public int BlockedPauseHours { get; set; } = 24;

    public string CalendarCode { get; set; } = "1000120";
    public string TeamsCode { get; set; } = "1000119";

    public string TimeZone { get; set; } = "Europe/Madrid";

    public int MatchLength { get; set; } = 90;
}
