using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.Players;

namespace TercerTiempo.Domain.Performances;

/// <summary>Rendimiento de un jugador en un partido concreto (una única ficha por jugador y partido).</summary>
public sealed class PlayerMatchPerformance : AggregateRoot
{
    public Guid PlayerId { get; private set; }
    public Guid MatchId { get; private set; }
    public Guid TeamId { get; private set; }
    public Guid SeasonId { get; private set; }

    public bool Started { get; private set; }
    public int MinutesPlayed { get; private set; }
    public int Goals { get; private set; }
    public int Assists { get; private set; }
    public int Shots { get; private set; }
    public int ShotsOnTarget { get; private set; }
    public int KeyPasses { get; private set; }
    public int Tackles { get; private set; }
    public int Interceptions { get; private set; }
    public int Saves { get; private set; }
    public int YellowCards { get; private set; }
    public bool RedCard { get; private set; }
    public int SelfRating { get; private set; }
    public string? Notes { get; private set; }

    public DateTimeOffset LoggedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public int GoalContributions => Goals + Assists;

    private PlayerMatchPerformance() { }

    public static PlayerMatchPerformance Log(Player player, Match match, Guid teamId, PerformanceStats stats, DateTimeOffset now)
    {
        PerformancePolicy.EnsureCanLog(player, match, teamId);
        stats.EnsureValid();

        var performance = new PlayerMatchPerformance
        {
            Id = NewId(),
            PlayerId = player.Id,
            MatchId = match.Id,
            TeamId = teamId,
            SeasonId = match.SeasonId,
            LoggedAt = now,
            UpdatedAt = now,
        };
        performance.Apply(stats);
        return performance;
    }

    public void Update(PerformanceStats stats, DateTimeOffset now)
    {
        stats.EnsureValid();
        Apply(stats);
        UpdatedAt = now;
    }

    public PerformanceStats ToStats() => new(
        Started, MinutesPlayed, Goals, Assists, Shots, ShotsOnTarget, KeyPasses,
        Tackles, Interceptions, Saves, YellowCards, RedCard, SelfRating, Notes);

    private void Apply(PerformanceStats stats)
    {
        Started = stats.Started;
        MinutesPlayed = stats.MinutesPlayed;
        Goals = stats.Goals;
        Assists = stats.Assists;
        Shots = stats.Shots;
        ShotsOnTarget = stats.ShotsOnTarget;
        KeyPasses = stats.KeyPasses;
        Tackles = stats.Tackles;
        Interceptions = stats.Interceptions;
        Saves = stats.Saves;
        YellowCards = stats.YellowCards;
        RedCard = stats.RedCard;
        SelfRating = stats.SelfRating;
        Notes = string.IsNullOrWhiteSpace(stats.Notes) ? null : stats.Notes.Trim();
    }
}
