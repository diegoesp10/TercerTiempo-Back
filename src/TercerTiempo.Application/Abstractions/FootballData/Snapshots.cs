using TercerTiempo.Domain.Competitions;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.Players;

namespace TercerTiempo.Application.Abstractions.FootballData;

// Instantáneas "planas" que devuelve el proveedor. La sincronización las convierte en entidades de dominio.

public sealed record CompetitionSnapshot(
    string ExternalId,
    string Name,
    string Category,
    CompetitionLevel Level,
    int Tier,
    string? Group,
    string Region,
    string SeasonExternalId,
    string SeasonLabel,
    DateOnly StartsOn,
    DateOnly EndsOn);

public sealed record MatchWindow(DateOnly From, DateOnly To)
{
    public bool Contains(DateOnly day) => day >= From && day <= To;
}

public sealed record TeamSnapshot(string ExternalId, string Name, string ShortName, string? CrestUrl);

public sealed record MatchSnapshot(
    string ExternalId,
    int Matchday,
    string HomeTeamExternalId,
    string AwayTeamExternalId,
    DateTimeOffset KickoffAt,
    MatchStatus Status,
    int? HomeGoals,
    int? AwayGoals,
    string? Venue);

public sealed record StandingSnapshot(
    string TeamExternalId,
    int Position,
    int Played,
    int Won,
    int Drawn,
    int Lost,
    int GoalsFor,
    int GoalsAgainst,
    int Points);

public sealed record RosterEntrySnapshot(string? ExternalId, string FullName, Position? Position, int? ShirtNumber);
