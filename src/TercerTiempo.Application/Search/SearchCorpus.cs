using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.Players;

namespace TercerTiempo.Application.Search;

// Lo que entra en el buscador, leído de la base de datos de una vez: ligas, equipos, clubes con cuenta, jugadores de
// las plantillas publicadas y partidos de las temporadas en curso, y las plantillas internas (que solo se enseñan a
// quien puede verlas).
public sealed record SearchCorpus(
    IReadOnlyList<CorpusCompetition> Competitions,
    IReadOnlyList<CorpusTeam> Teams,
    IReadOnlyList<CorpusClub> Clubs,
    IReadOnlyList<CorpusPlayer> Players,
    IReadOnlyList<CorpusMatch> Matches,
    IReadOnlyList<CorpusSquad> Squads);

public sealed record CorpusCompetition(
    Guid Id, string Name, string Category, string? Group, string Region, int Tier, int SortOrder,
    Guid? SeasonId, string? SeasonLabel, int Teams, int Matchdays, int Played);

// CompetitionId: la liga en la que juega esta temporada (null si no aparece en ninguna en curso).
public sealed record CorpusTeam(Guid Id, string Name, string ShortName, string? CrestUrl, Guid? CompetitionId, int? Position, int? Points);

public sealed record CorpusClub(Guid Id, string Name, IReadOnlyList<Guid> TeamIds);

public sealed record CorpusPlayer(Guid EntryId, Guid TeamId, string FullName, Position? Position, int? ShirtNumber, string? PhotoUrl);

public sealed record CorpusMatch(
    Guid Id, Guid CompetitionId, int Matchday, Guid HomeTeamId, Guid AwayTeamId, DateTimeOffset KickoffAt, MatchStatus Status, int? HomeGoals, int? AwayGoals);

public sealed record CorpusSquad(Guid Id, Guid TeamId, string SeasonLabel, IReadOnlyList<CorpusSquadPlayer> Players);

public sealed record CorpusSquadPlayer(Guid Id, string FullName, Position? Position, int? ShirtNumber, Guid? RosterEntryId, string? PhotoUrl);

public interface ISearchCorpusSource
{
    Task<SearchCorpus> LoadAsync(CancellationToken cancellationToken);
}
