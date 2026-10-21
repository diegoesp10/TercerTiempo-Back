using TercerTiempo.Domain.Competitions;

namespace TercerTiempo.Infrastructure.FootballData;

public sealed class FootballDataOptions
{
    public const string SectionName = "FootballData";

    public const string FutbolAsturianoProvider = "futbolasturiano";
    public const string FakeProvider = "fake";
    public const string JsonFilesProvider = "jsonfiles";
    public const string BeSoccerProvider = "besoccer";

    /// <summary>futbolasturiano | fake | jsonfiles | besoccer</summary>
    public string Provider { get; set; } = FutbolAsturianoProvider;

    public bool SyncOnStartup { get; set; } = true;

    /// <summary>0 desactiva la sincronización periódica.</summary>
    public int SyncIntervalHours { get; set; } = 6;

    public FutbolAsturianoOptions FutbolAsturiano { get; set; } = new();
    public JsonFilesOptions JsonFiles { get; set; } = new();
    public BeSoccerOptions BeSoccer { get; set; } = new();
}

/// <summary>Cliente de la API REST (WordPress + SportsPress) de futbolasturiano.es.</summary>
public sealed class FutbolAsturianoOptions
{
    public string BaseUrl { get; set; } = "https://www.futbolasturiano.es/wp-json/";

    /// <summary>Identificación honesta del bot ante el sitio.</summary>
    public string UserAgent { get; set; } = "TercerTiempo/1.0 (+https://github.com/diegoesp10/TercerTiempo-Back)";

    /// <summary>Etiqueta de la temporada a seguir ("2026/2027"). Vacío = la más reciente publicada.</summary>
    public string Season { get; set; } = string.Empty;

    /// <summary>Mes (1-12) en que empieza y termina la temporada, para fechar las temporadas a partir de su etiqueta.</summary>
    public int SeasonStartMonth { get; set; } = 8;
    public int SeasonEndMonth { get; set; } = 6;

    public string Region { get; set; } = "Asturias";

    /// <summary>Pausa entre peticiones para no castigar al sitio.</summary>
    public int RequestDelayMs { get; set; } = 150;

    public int PageSize { get; set; } = 100;

    /// <summary>Zona horaria en la que el sitio guarda la fecha y hora de los partidos (campo <c>date</c>).</summary>
    public string TimeZone { get; set; } = "Europe/Madrid";

    /// <summary>
    /// Recuperar los partidos que la API oculta por estar "programados" en WordPress (la jornada en curso y parte del
    /// calendario futuro) leyendo su página pública.
    /// </summary>
    public bool RecoverHiddenEvents { get; set; } = true;

    /// <summary>Máximo de páginas de partido que se descargan por liga en cada sincronización (cada una tarda segundos).</summary>
    public int HiddenEventPagesPerLeague { get; set; } = 30;

    /// <summary>Los partidos ocultos ya conocidos que se juegan en los próximos N días se vuelven a leer (hora definitiva, resultado).</summary>
    public int HiddenEventRefreshDays { get; set; } = 7;

    // Niveles que se sincronizan. Por defecto, la pirámide amateur asturiana: 1ª/2ª/3ª RFEF y 1ª/2ª/3ª AsturFútbol.
    public bool IncludeProfessional { get; set; }
    public bool IncludeNational { get; set; } = true;
    public bool IncludeRegional { get; set; } = true;
    public bool IncludeCups { get; set; }
    public bool IncludeYouth { get; set; }

    /// <summary>Ids de liga (término sp_league) que se incluyen aunque su nivel esté desactivado.</summary>
    public int[] IncludeLeagueIds { get; set; } = [];

    /// <summary>Ids de liga (término sp_league) que nunca se sincronizan.</summary>
    public int[] ExcludeLeagueIds { get; set; } = [];
}

public sealed class JsonFilesOptions
{
    /// <summary>
    /// Carpeta con competitions.json, competitions/{competitionId}/{teams,matches,standings}.json y rosters/{teamId}.json.
    /// </summary>
    public string Directory { get; set; } = "data/football";
}

public sealed class BeSoccerOptions
{
    public string BaseUrl { get; set; } = "https://apiclient.besoccerapps.com/scripts/api/api.php";
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Identificador de la liga en BeSoccer (parámetro "league").</summary>
    public string CompetitionId { get; set; } = string.Empty;
    public string CompetitionName { get; set; } = string.Empty;
    public CompetitionLevel Level { get; set; } = CompetitionLevel.Regional;
    public int Tier { get; set; } = 1;
    public string? Group { get; set; }
    public string Region { get; set; } = "Asturias";

    /// <summary>Temporada en BeSoccer (parámetro "year"), p. ej. "2026".</summary>
    public string Season { get; set; } = string.Empty;
    public string SeasonLabel { get; set; } = string.Empty;
    public DateOnly SeasonStartsOn { get; set; }
    public DateOnly SeasonEndsOn { get; set; }
}
