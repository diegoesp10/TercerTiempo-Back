namespace TercerTiempo.Infrastructure.FootballData;

public sealed class FootballDataOptions
{
    public const string SectionName = "FootballData";

    public const string FakeProvider = "fake";
    public const string JsonFilesProvider = "jsonfiles";
    public const string BeSoccerProvider = "besoccer";

    /// <summary>fake | jsonfiles | besoccer</summary>
    public string Provider { get; set; } = FakeProvider;

    public bool SyncOnStartup { get; set; } = true;

    /// <summary>0 desactiva la sincronización periódica.</summary>
    public int SyncIntervalHours { get; set; } = 6;

    public JsonFilesOptions JsonFiles { get; set; } = new();
    public BeSoccerOptions BeSoccer { get; set; } = new();
}

public sealed class JsonFilesOptions
{
    /// <summary>Carpeta con competition.json, teams.json, matches.json, standings.json y rosters/{teamId}.json.</summary>
    public string Directory { get; set; } = "data/football";
}

public sealed class BeSoccerOptions
{
    public string BaseUrl { get; set; } = "https://apiclient.besoccerapps.com/scripts/api/api.php";
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Identificador de la liga en BeSoccer (parámetro "league").</summary>
    public string CompetitionId { get; set; } = string.Empty;
    public string CompetitionName { get; set; } = string.Empty;
    public string Region { get; set; } = "Asturias";

    /// <summary>Temporada en BeSoccer (parámetro "year"), p. ej. "2026".</summary>
    public string Season { get; set; } = string.Empty;
    public string SeasonLabel { get; set; } = string.Empty;
    public DateOnly SeasonStartsOn { get; set; }
    public DateOnly SeasonEndsOn { get; set; }
}
