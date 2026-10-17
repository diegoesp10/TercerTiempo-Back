using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.Squads;

namespace TercerTiempo.Domain.Performances;

/// <summary>
/// Línea del acta: rendimiento de una ficha de la plantilla en un partido, registrado por el cuerpo técnico.
/// Una única línea por ficha y partido.
/// </summary>
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

    /// <summary>Valoración del cuerpo técnico, de 1 a 10.</summary>
    public int Rating { get; private set; }

    public string? Notes { get; private set; }

    public Guid RecordedByCoachId { get; private set; }
    public DateTimeOffset LoggedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public int GoalContributions => Goals + Assists;

    private PlayerMatchPerformance() { }

    public static PlayerMatchPerformance Record(
        Squad squad, SquadPlayer player, Match match, Guid coachId, PerformanceStats stats, DateTimeOffset now)
    {
        PerformancePolicy.EnsureCanRecord(squad, coachId, match);

        if (player.SquadId != squad.Id)
            throw new DomainException("El jugador no pertenece a esta plantilla.");

        stats.EnsureValid();

        var performance = new PlayerMatchPerformance
        {
            Id = NewId(),
            SquadId = squad.Id,
            SquadPlayerId = player.Id,
            MatchId = match.Id,
            TeamId = squad.TeamId,
            SeasonId = match.SeasonId,
            RecordedByCoachId = coachId,
            LoggedAt = now,
            UpdatedAt = now,
        };
        performance.Apply(stats);
        return performance;
    }

    public void Update(PerformanceStats stats, Guid coachId, DateTimeOffset now)
    {
        stats.EnsureValid();
        Apply(stats);
        RecordedByCoachId = Guard.NotEmpty(coachId, nameof(coachId));
        UpdatedAt = now;
    }

    public PerformanceStats ToStats() => new(
        Started, MinutesPlayed, Goals, Assists, Shots, ShotsOnTarget, KeyPasses,
        Tackles, Interceptions, Saves, YellowCards, RedCard, Rating, Notes);

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
        Rating = stats.Rating;
        Notes = string.IsNullOrWhiteSpace(stats.Notes) ? null : stats.Notes.Trim();
    }
}
