using TercerTiempo.Domain.OfficialSheets;

namespace TercerTiempo.Application.Abstractions.OfficialSheets;

/// <summary>
/// Fuente de las actas oficiales (la web de la federación). Localiza la competición en su catálogo, lista los partidos
/// de una jornada con el código de su acta y devuelve el contenido de un acta.
/// </summary>
public interface IOfficialSheetProvider
{
    string ProviderName { get; }

    /// <summary>Competición, grupo y temporada en el proveedor que corresponden a una de nuestras ligas; null si no se encuentra.</summary>
    Task<OfficialCompetitionRef?> FindCompetitionAsync(string category, string? group, string seasonLabel, CancellationToken cancellationToken);

    /// <summary>Partidos de una jornada con el código de su acta (si ya está publicada).</summary>
    Task<IReadOnlyList<OfficialFixture>> GetMatchdayAsync(OfficialCompetitionRef competition, int matchday, CancellationToken cancellationToken);

    /// <summary>Contenido del acta; sin líneas si todavía no se ha cerrado; null si la página no existe.</summary>
    Task<OfficialSheetData?> GetSheetAsync(string sheetCode, CancellationToken cancellationToken);

    /// <summary>Página pública del acta en el proveedor.</summary>
    string SheetUrl(string sheetCode);
}

/// <summary>Códigos de competición, grupo y temporada en el proveedor; se guardan como "competición/grupo/temporada".</summary>
public sealed record OfficialCompetitionRef(string CompetitionCode, string GroupCode, string SeasonCode)
{
    public override string ToString() => $"{CompetitionCode}/{GroupCode}/{SeasonCode}";

    public static OfficialCompetitionRef? Parse(string? value)
    {
        var parts = (value ?? string.Empty).Split('/');
        return parts.Length == 3 && parts.All(p => p.Length > 0) ? new OfficialCompetitionRef(parts[0], parts[1], parts[2]) : null;
    }
}

/// <summary>Partido de una jornada según el proveedor: nombres y códigos de los equipos, fecha local y código del acta.</summary>
public sealed record OfficialFixture(
    string HomeName,
    string? HomeCode,
    string AwayName,
    string? AwayCode,
    DateOnly? Date,
    TimeOnly? Time,
    string? SheetCode);
