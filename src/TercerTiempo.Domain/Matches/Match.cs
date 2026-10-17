using TercerTiempo.Domain.Common;

namespace TercerTiempo.Domain.Matches;

/// <summary>Partido de liga entre dos equipos de una temporada. Lo alimenta el proveedor externo.</summary>
public sealed class Match : AggregateRoot
{
    // El marcador se persiste como dos columnas (HomeGoals/AwayGoals) y se expone como value object.
    private int? _homeGoals;
    private int? _awayGoals;

    public Guid SeasonId { get; private set; }
    public int Matchday { get; private set; }
    public Guid HomeTeamId { get; private set; }
    public Guid AwayTeamId { get; private set; }
    public DateTimeOffset KickoffAt { get; private set; }
    public MatchStatus Status { get; private set; }
    public string? Venue { get; private set; }
    public ExternalRef? ExternalRef { get; private set; }

    public Score? Score => _homeGoals is { } home && _awayGoals is { } away ? Score.Create(home, away) : null;

    private Match() { }

    public static Match Schedule(
        Guid seasonId, int matchday, Guid homeTeamId, Guid awayTeamId, DateTimeOffset kickoffAt, string? venue, ExternalRef? externalRef)
    {
        if (homeTeamId == awayTeamId)
            throw new DomainException("Un equipo no puede jugar contra sí mismo.");

        return new Match
        {
            Id = NewId(),
            SeasonId = Guard.NotEmpty(seasonId, nameof(seasonId)),
            Matchday = Guard.InRange(matchday, 1, 60, "jornada"),
            HomeTeamId = Guard.NotEmpty(homeTeamId, nameof(homeTeamId)),
            AwayTeamId = Guard.NotEmpty(awayTeamId, nameof(awayTeamId)),
            KickoffAt = kickoffAt,
            Status = MatchStatus.Scheduled,
            Venue = venue,
            ExternalRef = externalRef,
        };
    }

    public void RecordResult(int homeGoals, int awayGoals, DateTimeOffset now)
    {
        if (Status == MatchStatus.Cancelled)
            throw new DomainException("No se puede registrar el resultado de un partido cancelado.");

        var score = Score.Create(homeGoals, awayGoals);
        var wasPlayed = Status == MatchStatus.Played;

        _homeGoals = score.Home;
        _awayGoals = score.Away;
        Status = MatchStatus.Played;

        if (!wasPlayed)
            RaiseDomainEvent(new MatchPlayedEvent(Id, now));
    }

    public void Reschedule(DateTimeOffset kickoffAt)
    {
        if (Status == MatchStatus.Played)
            throw new DomainException("No se puede reprogramar un partido ya disputado.");

        KickoffAt = kickoffAt;
        Status = MatchStatus.Scheduled;
    }

    public void Postpone()
    {
        if (Status == MatchStatus.Played)
            throw new DomainException("No se puede aplazar un partido ya disputado.");

        Status = MatchStatus.Postponed;
    }

    public void Cancel()
    {
        if (Status == MatchStatus.Played)
            throw new DomainException("No se puede cancelar un partido ya disputado.");

        Status = MatchStatus.Cancelled;
        _homeGoals = null;
        _awayGoals = null;
    }

    public void MarkInProgress()
    {
        if (Status is MatchStatus.Played or MatchStatus.Cancelled)
            throw new DomainException("El partido no puede pasar a 'en juego' desde su estado actual.");

        Status = MatchStatus.InProgress;
    }

    public bool Involves(Guid teamId) => HomeTeamId == teamId || AwayTeamId == teamId;

    public bool IsHome(Guid teamId) => HomeTeamId == teamId;

    public Guid OpponentOf(Guid teamId)
    {
        if (teamId == HomeTeamId) return AwayTeamId;
        if (teamId == AwayTeamId) return HomeTeamId;
        throw new DomainException("El equipo no participa en este partido.");
    }

    public int? GoalsFor(Guid teamId) => Score is null ? null : IsHome(teamId) ? Score.Home : Score.Away;

    public int? GoalsAgainst(Guid teamId) => Score is null ? null : IsHome(teamId) ? Score.Away : Score.Home;

    public MatchOutcome OutcomeFor(Guid teamId)
    {
        if (Status != MatchStatus.Played || Score is not { } score || !Involves(teamId))
            return MatchOutcome.NotPlayed;

        var goalsFor = IsHome(teamId) ? score.Home : score.Away;
        var goalsAgainst = IsHome(teamId) ? score.Away : score.Home;

        return goalsFor > goalsAgainst ? MatchOutcome.Win
            : goalsFor == goalsAgainst ? MatchOutcome.Draw
            : MatchOutcome.Loss;
    }

    public int? PointsFor(Guid teamId) => OutcomeFor(teamId) switch
    {
        MatchOutcome.Win => 3,
        MatchOutcome.Draw => 1,
        MatchOutcome.Loss => 0,
        _ => null,
    };
}
