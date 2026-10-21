namespace TercerTiempo.Infrastructure.OfficialSheets.Asturfutbol;

/// <summary>Sección "OfficialSheets" de la configuración: lectura de las actas en asturfutbol.es.</summary>
public sealed class OfficialSheetOptions
{
    public const string SectionName = "OfficialSheets";

    public bool Enabled { get; set; } = true;

    public AsturfutbolOptions Asturfutbol { get; set; } = new();
}

/// <summary>
/// Web de la Real Federación de Fútbol del Principado de Asturias. Las páginas son públicas pero exigen una cookie de
/// sesión: la primera petición redirige a <c>/pnfg/NLogin</c>, que la fija y devuelve a la página pedida.
/// </summary>
public sealed class AsturfutbolOptions
{
    public string BaseUrl { get; set; } = "https://www.asturfutbol.es/pnfg/NPcd/";

    public string UserAgent { get; set; } = "TercerTiempo/1.0 (+https://github.com/diegoesp10/TercerTiempo-Back)";

    /// <summary>Pausa mínima entre peticiones: es una web "clásica" sin API y no conviene castigarla.</summary>
    public int RequestDelayMs { get; set; } = 2000;

    /// <summary>Parámetro cod_primaria de cada sección de la web.</summary>
    public string CalendarCode { get; set; } = "1000120";
    public string TeamsCode { get; set; } = "1000119";

    /// <summary>Código de primaria de la página de campos (<c>NFG_VisCampos</c>).</summary>
    public string GroundsCode { get; set; } = "1000122";

    /// <summary>Zona horaria de las fechas que publica la web.</summary>
    public string TimeZone { get; set; } = "Europe/Madrid";

    /// <summary>Minutos reglamentarios para calcular los minutos jugados de quien termina el partido en el campo.</summary>
    public int MatchLength { get; set; } = 90;
}
