using TercerTiempo.Domain.OfficialSheets;

namespace TercerTiempo.Application.Abstractions.OfficialSheets;

public interface IOfficialSheetProvider
{
    string ProviderName { get; }

    Task<OfficialCompetitionRef?> FindCompetitionAsync(string category, string? group, string seasonLabel, CancellationToken cancellationToken);

    Task<IReadOnlyList<OfficialFixture>> GetMatchdayAsync(OfficialCompetitionRef competition, int matchday, CancellationToken cancellationToken);

    Task<OfficialSheetData?> GetSheetAsync(string sheetCode, CancellationToken cancellationToken);

    string SheetUrl(string sheetCode);
}

public sealed class ProviderThrottledException(string provider, string message) : HttpRequestException(message)
{
    public string Provider { get; } = provider;
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
    string? SheetCode);
