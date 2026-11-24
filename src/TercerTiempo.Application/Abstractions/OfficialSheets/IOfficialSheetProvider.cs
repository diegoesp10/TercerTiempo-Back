using TercerTiempo.Domain.OfficialSheets;
using TercerTiempo.Domain.Referees;
using TercerTiempo.Domain.Sanctions;

namespace TercerTiempo.Application.Abstractions.OfficialSheets;

public interface IOfficialSheetProvider
{
    string ProviderName { get; }

    // Si la web tiene las actas de esa categoría; si no, no se le pregunta (ni se gastan peticiones).
    bool Publishes(string category);

    Task<OfficialCompetitionRef?> FindCompetitionAsync(string category, string? group, string seasonLabel, CancellationToken cancellationToken);

    // maxAge: la página guardada solo vale si se descargó hace menos de eso (los árbitros designados cambian a diario).
    Task<IReadOnlyList<OfficialFixture>> GetMatchdayAsync(OfficialCompetitionRef competition, int matchday, TimeSpan? maxAge, CancellationToken cancellationToken);

    // refresh: no vale la copia guardada de la página (el acta estaba sin cerrar); se pide de nuevo respetando los límites.
    Task<OfficialSheetData?> GetSheetAsync(string sheetCode, bool refresh, CancellationToken cancellationToken);

    // El acta tal como se descargó la última vez, sin pedir nada a la web (null si no está guardada).
    Task<OfficialSheetData?> GetCachedSheetAsync(string sheetCode, CancellationToken cancellationToken);

    string SheetUrl(string sheetCode);

    // La página de sanciones del grupo con todo lo que lleva publicado el comité de competición en la temporada. Se pide
    // siempre de nuevo (con los límites de siempre); null si la web no la tiene.
    Task<IReadOnlyList<CommitteeMeetingData>?> GetSanctionsAsync(OfficialCompetitionRef competition, CancellationToken cancellationToken);
}

// Lo que publicó el comité sobre una jornada en una reunión: solo las sanciones de jugadores que cuestan partidos.
public sealed record CommitteeMeetingData(
    string Committee, DateOnly MeetingOn, int Matchday, DateOnly? MatchdayOn, IReadOnlyList<CommitteeSanctionData> Sanctions);

public sealed class ProviderThrottledException(string provider, string message, DateTimeOffset? retryAt = null) : HttpRequestException(message)
{
    public string Provider { get; } = provider;
    public DateTimeOffset? RetryAt { get; } = retryAt;
}

public sealed record OfficialCompetitionRef(string CompetitionCode, string GroupCode, string SeasonCode)
{
    public override string ToString() => $"{CompetitionCode}/{GroupCode}/{SeasonCode}";

    public static OfficialCompetitionRef? Parse(string? value)
    {
        var parts = (value ?? string.Empty).Split('/');
        return parts.Length == 3 && parts.All(p => p.Length > 0) ? new OfficialCompetitionRef(parts[0], parts[1], parts[2]) : null;
    }
}

public sealed record OfficialFixture(
    string HomeName,
    string? HomeCode,
    string AwayName,
    string? AwayCode,
    DateOnly? Date,
    TimeOnly? Time,
    string? SheetCode,
    // El equipo arbitral que publica la página de la jornada (antes del partido, el designado; de momento solo el principal).
    IReadOnlyList<MatchOfficialData> Officials);
