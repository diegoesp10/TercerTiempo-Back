namespace TercerTiempo.Infrastructure.OfficialSheets.Asturfutbol;

public sealed class OfficialSheetOptions
{
    public const string SectionName = "OfficialSheets";

    public bool Enabled { get; set; } = true;

    // Descarga en segundo plano: cada cuántos minutos se buscan actas que falten o toque refrescar, cuántas como
    // máximo por pasada y cuánto se espera tras arrancar antes de la primera.
    public int BackgroundScanMinutes { get; set; } = 30;

    public int BackgroundBatch { get; set; } = 400;

    public int BackgroundStartDelaySeconds { get; set; } = 20;

    public AsturfutbolOptions Asturfutbol { get; set; } = new();
}

public sealed class AsturfutbolOptions
{
    public string BaseUrl { get; set; } = "https://www.asturfutbol.es/pnfg/NPcd/";

    public string UserAgent { get; set; } = "TercerTiempo/1.0 (+https://github.com/diegoesp10/TercerTiempo-Back)";

    public int RequestDelayMs { get; set; } = 2000;

    public string CalendarCode { get; set; } = "1000120";
    public string TeamsCode { get; set; } = "1000119";

    public string GroundsCode { get; set; } = "1000122";

    public string TimeZone { get; set; } = "Europe/Madrid";

    public int MatchLength { get; set; } = 90;
}
