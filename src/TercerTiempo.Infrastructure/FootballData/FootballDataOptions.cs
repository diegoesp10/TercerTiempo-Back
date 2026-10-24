namespace TercerTiempo.Infrastructure.FootballData;

public sealed class FootballDataOptions
{
    public const string SectionName = "FootballData";

    public const string FutbolAsturianoProvider = "futbolasturiano";
    public const string FakeProvider = "fake";
    public const string JsonFilesProvider = "jsonfiles";

    public string Provider { get; set; } = FutbolAsturianoProvider;

    // Minutos entre sincronizaciones, contados desde la última guardada en JobRuns (arrancar la API no la repite).
    // 0 = solo a mano desde /jobs.
    public int SyncIntervalMinutes { get; set; } = 60;

    public FutbolAsturianoOptions FutbolAsturiano { get; set; } = new();
    public JsonFilesOptions JsonFiles { get; set; } = new();
}

public sealed class FutbolAsturianoOptions
{
    public string BaseUrl { get; set; } = "https://www.futbolasturiano.es/wp-json/";

    public string UserAgent { get; set; } = "TercerTiempo/1.0 (+https://github.com/diegoesp10/TercerTiempo-Back)";

    public string Season { get; set; } = string.Empty;

    public int SeasonStartMonth { get; set; } = 8;
    public int SeasonEndMonth { get; set; } = 6;

    public string Region { get; set; } = "Asturias";

    public int RequestDelayMs { get; set; } = 150;

    public int PageSize { get; set; } = 100;

    public string TimeZone { get; set; } = "Europe/Madrid";

    public bool RecoverHiddenEvents { get; set; } = true;

    public int HiddenEventPagesPerLeague { get; set; } = 30;

    public int HiddenEventRefreshDays { get; set; } = 7;

    // Niveles que se sincronizan. Por defecto, la pirámide amateur asturiana: 1ª/2ª/3ª RFEF y 1ª/2ª/3ª AsturFútbol.
    public bool IncludeProfessional { get; set; }
    public bool IncludeNational { get; set; } = true;
    public bool IncludeRegional { get; set; } = true;
    public bool IncludeCups { get; set; }
    public bool IncludeYouth { get; set; }

    public int[] IncludeLeagueIds { get; set; } = [];

    public int[] ExcludeLeagueIds { get; set; } = [];
}

public sealed class JsonFilesOptions
{
    public string Directory { get; set; } = "data/football";
}
