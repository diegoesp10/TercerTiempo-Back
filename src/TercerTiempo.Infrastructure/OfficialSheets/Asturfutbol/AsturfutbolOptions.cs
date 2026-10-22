namespace TercerTiempo.Infrastructure.OfficialSheets.Asturfutbol;

public sealed class OfficialSheetOptions
{
    public const string SectionName = "OfficialSheets";

    public bool Enabled { get; set; } = true;

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
