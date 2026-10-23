using TercerTiempo.Domain.OfficialSheets;

namespace TercerTiempo.Application.Abstractions.OfficialSheets;

// Web de resultados que publica el acta de cada partido (BeSoccer). A diferencia de la federación, el partido se
// localiza por competición, grupo, equipo y fecha.
public interface IMatchSheetDirectory
{
    string ProviderName { get; }

    // Códigos de los grupos de la competición en esa temporada (uno solo si no tiene grupos); vacío si no la cubre.
    Task<IReadOnlyList<string>> GetGroupsAsync(string category, string seasonLabel, CancellationToken cancellationToken);

    Task<IReadOnlyList<DirectoryTeam>> GetTeamsAsync(string category, string seasonLabel, string group, CancellationToken cancellationToken);

    Task<IReadOnlyList<DirectoryFixture>> GetFixturesAsync(string teamCode, CancellationToken cancellationToken);

    // null o sin líneas mientras no estén publicadas las alineaciones.
    Task<OfficialSheetData?> GetSheetAsync(string matchCode, CancellationToken cancellationToken);

    string SheetUrl(string matchCode);
}

public sealed record DirectoryTeam(string Code, string Name);

public sealed record DirectoryFixture(string Code, string HomeCode, string HomeName, string AwayCode, string AwayName, DateTimeOffset? StartsAt);
