namespace TercerTiempo.Infrastructure.OfficialSheets.BeSoccer;

public sealed class BeSoccerOptions
{
    public bool Enabled { get; set; } = true;

    public string BaseUrl { get; set; } = "https://es.besoccer.com/";

    public string UserAgent { get; set; } = "TercerTiempo/1.0 (+https://github.com/diegoesp10/TercerTiempo-Back)";

    // Sus páginas salen de una CDN, pero aun así se piden de una en una y con tope por hora y día.
    public int RequestDelayMs { get; set; } = 8000;
    public int MaxRequestsPerHour { get; set; } = 150;
    public int MaxRequestsPerDay { get; set; } = 1200;

    // Cuánto vale cada página guardada: grupos y equipos de una competición, partidos de un equipo y páginas del acta
    // (el acta completa queda en SQL y no se vuelve a pedir).
    public int CatalogCacheDays { get; set; } = 30;
    public int FixturesCacheHours { get; set; } = 6;
    public int SheetCacheHours { get; set; } = 3;

    public int FailureRetryMinutes { get; set; } = 30;
    public int BlockedPauseHours { get; set; } = 6;

    public int MatchLength { get; set; } = 90;

    // Nuestra categoría de competición → competición en BeSoccer. Su temporada 2026/2027 es la «2027» de la URL y sus
    // grupos se eligen por los equipos que tienen en común con los nuestros.
    public Dictionary<string, string> Competitions { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
