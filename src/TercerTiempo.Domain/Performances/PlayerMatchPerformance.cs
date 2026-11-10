using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.Players;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Common.Errors;

namespace TercerTiempo.Domain.Performances;

public sealed class PlayerMatchPerformance : AggregateRoot
{
    public Guid SquadId { get; private set; }
    public Guid SquadPlayerId { get; private set; }
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
    public int Fouls { get; private set; }
    public int BallLosses { get; private set; }
    public int Recoveries { get; private set; }
    public int PassesCompleted { get; private set; }
    public int PassesAttempted { get; private set; }

    public double? Rating { get; private set; }

    public string? Notes { get; private set; }

    public Guid? RecordedByCoachId { get; private set; }
    public Guid? RecordedByClubId { get; private set; }
    public DateTimeOffset LoggedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public int? ShirtNumber { get; private set; }

    public int GoalContributions => Goals + Assists;

    public void SetShirtNumber(int? shirtNumber)
    {
        if (shirtNumber is < 1 or > 99)
            throw new DomainException(ErrorCode.PlayerShirtNumberRange);

        ShirtNumber = shirtNumber;
    }

    private PlayerMatchPerformance() { }

    public static PlayerMatchPerformance Record(
        Squad squad, SquadPlayer player, Match match, SquadEditor editor, PerformanceStats stats, DateTimeOffset now)
    {
        PerformancePolicy.EnsureCanRecord(squad, editor, match, Squad.Today(now));

        if (player.SquadId != squad.Id)
            throw new DomainException(ErrorCode.SquadPlayerNotMember);

        stats.EnsureValid();

        var performance = new PlayerMatchPerformance
        {
            Id = NewId(),
            SquadId = squad.Id,
            SquadPlayerId = player.Id,
            MatchId = match.Id,
            TeamId = squad.TeamId,
            SeasonId = match.SeasonId,
            RecordedByCoachId = editor.CoachId,
            RecordedByClubId = editor.ClubId,
            LoggedAt = now,
            UpdatedAt = now,
        };
        performance.Apply(stats);
        performance.Recalculate(player, match);
        return performance;
    }

    public void Update(PerformanceStats stats, SquadPlayer player, Match match, SquadEditor editor, DateTimeOffset now)
    {
        stats.EnsureValid();
        Apply(stats);
        Recalculate(player, match);
        RecordedByCoachId = editor.CoachId;
        RecordedByClubId = editor.ClubId;
        UpdatedAt = now;
    }

    public RatingBreakdown? Recalculate(SquadPlayer player, Match match)
    {
        if (player.Id != SquadPlayerId || match.Id != MatchId)
            throw new DomainException(ErrorCode.SheetLineMismatch);

        var breakdown = Breakdown(player.Position, match);
        Rating = breakdown?.Total;
        return breakdown;
    }

    public RatingBreakdown? Breakdown(Position? position, Match match) =>
        PerformanceRating.Calculate(ToStats(), position, match.GoalsFor(TeamId) ?? 0, match.GoalsAgainst(TeamId) ?? 0);

    public PerformanceStats ToStats() => new(
        Started, MinutesPlayed, Goals, Assists, Shots, ShotsOnTarget, KeyPasses,
        Tackles, Interceptions, Saves, YellowCards, RedCard, Notes, Fouls,
        BallLosses, Recoveries, PassesCompleted, PassesAttempted);

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
        Fouls = stats.Fouls;
        BallLosses = stats.BallLosses;
        Recoveries = stats.Recoveries;
        PassesCompleted = stats.PassesCompleted;
        PassesAttempted = stats.PassesAttempted;
        Notes = string.IsNullOrWhiteSpace(stats.Notes) ? null : stats.Notes.Trim();
    }
}
