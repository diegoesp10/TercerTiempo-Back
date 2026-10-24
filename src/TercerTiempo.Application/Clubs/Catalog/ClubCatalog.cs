using TercerTiempo.Application.Teams;
using System.ComponentModel;
using System.Text.RegularExpressions;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Auth;
using TercerTiempo.Application.OfficialSheets;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Teams;

namespace TercerTiempo.Application.Clubs.Catalog;

public sealed record ClubCatalogDto(
    string Name,
    bool Registered,
    IReadOnlyList<ClubCatalogTeamDto> Teams,
    [property: Description("Escudo del primer equipo del club.")]
    string? CrestUrl = null);

public sealed record ClubCatalogTeamDto(Guid Id, string Name, string? CompetitionName);

public sealed record GetClubCatalogQuery(
    [property: Description("Texto a buscar en el nombre del club o de sus equipos (opcional).")]
    string? Search) : IQuery<IReadOnlyList<ClubCatalogDto>>;

internal sealed class GetClubCatalogHandler(AccountStores stores) : IQueryHandler<GetClubCatalogQuery, IReadOnlyList<ClubCatalogDto>>
{
    public async Task<IReadOnlyList<ClubCatalogDto>> Handle(GetClubCatalogQuery query, CancellationToken cancellationToken)
    {
        var catalog = await ClubCatalog.LoadAsync(stores, cancellationToken);
        var registered = (await stores.Clubs.GetRegisteredNamesAsync(cancellationToken)).ToHashSet();
        var search = NameNormalizer.Normalize(query.Search);

        return catalog
            .Where(entry => search.Length == 0
                || entry.NormalizedName.Contains(search)
                || entry.Teams.Any(t => NameNormalizer.Normalize(t.Team.Name).Contains(search)))
            .Select(entry => new ClubCatalogDto(
                entry.Name,
                registered.Contains(entry.NormalizedName),
                entry.Teams.Select(t => new ClubCatalogTeamDto(t.Team.Id, t.Team.Name, t.CompetitionName)).ToList(),
                entry.Teams[0].Team.CrestLink()))
            .ToList();
    }
}

public sealed record CatalogTeam(Team Team, string? CompetitionName);

public sealed record ClubCatalogEntry(string Name, string NormalizedName, IReadOnlyList<CatalogTeam> Teams);

internal static partial class ClubCatalog
{
    public static async Task<IReadOnlyList<ClubCatalogEntry>> LoadAsync(AccountStores stores, CancellationToken cancellationToken)
    {
        // Competición en curso de cada equipo: una consulta por temporada activa en lugar de una por equipo.
        var competitionByTeam = new Dictionary<Guid, string>();
        foreach (var competition in await stores.Competitions.GetAllAsync(cancellationToken))
        {
            if (competition.CurrentSeason is not { } season) continue;
            foreach (var team in await stores.Teams.GetBySeasonAsync(season.Id, cancellationToken))
                competitionByTeam.TryAdd(team.Id, competition.Name);
        }

        var teams = await stores.Teams.GetAllAsync(cancellationToken);
        return Build(teams.Select(t => new CatalogTeam(t, competitionByTeam.GetValueOrDefault(t.Id))));
    }

    public static ClubCatalogEntry? ForTeam(IReadOnlyList<ClubCatalogEntry> catalog, Guid teamId) =>
        catalog.FirstOrDefault(entry => entry.Teams.Any(t => t.Team.Id == teamId));

    // El club del catálogo de una cuenta de club: el de su nombre (se elige del catálogo al darse de alta) o, si el
    // proveedor lo ha renombrado, el de alguno de sus equipos.
    public static ClubCatalogEntry? ForClub(IReadOnlyList<ClubCatalogEntry> catalog, Domain.Clubs.Club club) =>
        catalog.FirstOrDefault(entry => entry.NormalizedName == club.NormalizedName)
        ?? club.Teams.Select(t => ForTeam(catalog, t.TeamId)).FirstOrDefault(entry => entry is not null);

    public static IReadOnlyList<ClubCatalogEntry> Build(IEnumerable<CatalogTeam> teams)
    {
        var groups = new List<Group>();
        foreach (var team in teams)
        {
            var (core, suffix) = TeamNameSimilarity.Split(team.Team.Name);
            if (core.Count == 0) continue;

            var key = string.Join(' ', core.Order(StringComparer.Ordinal));
            var group = groups.FirstOrDefault(g => g.Key == key);
            if (group is null)
            {
                group = new Group(key, core);
                groups.Add(group);
            }
            group.Teams.Add((team, suffix));
        }

        // Un filial con el nombre abreviado ("CD Lealtad 'B'") se une al club cuyo nombre lo contiene, si solo hay uno.
        foreach (var group in groups.Where(g => g.Teams.All(t => t.Suffix is not null)).ToList())
        {
            var parents = groups.Where(g => g != group && group.Core.IsProperSubsetOf(g.Core)).ToList();
            if (parents.Count != 1) continue;

            parents[0].Teams.AddRange(group.Teams);
            groups.Remove(group);
        }

        return groups
            .Select(group =>
            {
                var ordered = group.Teams
                    .OrderBy(t => t.Suffix is null ? 0 : 1)
                    .ThenBy(t => t.Suffix, StringComparer.Ordinal)
                    .ThenBy(t => t.Team.Team.Name, StringComparer.Ordinal)
                    .ToList();
                var name = ClubName(ordered);
                return new ClubCatalogEntry(name, NameNormalizer.Normalize(name), ordered.Select(t => t.Team).ToList());
            })
            .OrderBy(entry => entry.Name, StringComparer.Create(System.Globalization.CultureInfo.GetCultureInfo("es-ES"), ignoreCase: true))
            .ToList();
    }

    private static string ClubName(List<(CatalogTeam Team, string? Suffix)> ordered)
    {
        var first = ordered.FirstOrDefault(t => t.Suffix is null).Team ?? ordered[0].Team;
        return ordered.Any(t => t.Suffix is null) ? first.Team.Name : ReserveSuffix().Replace(first.Team.Name, string.Empty).Trim();
    }

    [GeneratedRegex("""\s*["'«»“”‘’]?[A-Za-z]["'«»“”‘’]?$""")]
    private static partial Regex ReserveSuffix();

    private sealed class Group(string key, HashSet<string> core)
    {
        public string Key { get; } = key;
        public HashSet<string> Core { get; } = core;
        public List<(CatalogTeam Team, string? Suffix)> Teams { get; } = [];
    }
}
