using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Common.Errors;

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

    /// <summary>Acta del partido en la federación (asturfutbol.es), cuando ya se ha localizado.</summary>
    public ExternalRef? OfficialRef { get; private set; }

    public Score? Score => _homeGoals is { } home && _awayGoals is { } away ? Score.Create(home, away) : null;

    private Match() { }

    public static Match Schedule(
        Guid seasonId, int matchday, Guid homeTeamId, Guid awayTeamId, DateTimeOffset kickoffAt, string? venue, ExternalRef? externalRef)
    {
        if (homeTeamId == awayTeamId)
            throw new DomainException(ErrorCode.MatchSameTeam);

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
            throw new DomainException(ErrorCode.MatchResultOnCancelled);

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
            throw new DomainException(ErrorCode.MatchReschedulePlayed);

        KickoffAt = kickoffAt;
        Status = MatchStatus.Scheduled;
    }

    /// <summary>
    /// Datos de calendario según el proveedor (jornada, fecha y hora, campo). Se pueden corregir en cualquier estado,
    /// también con el partido ya disputado; el estado y el marcador no cambian.
    /// </summary>
    public void UpdateFixture(int matchday, DateTimeOffset kickoffAt, string? venue)
    {
        Matchday = Guard.InRange(matchday, 1, 60, "jornada");
        KickoffAt = kickoffAt;
        Venue = string.IsNullOrWhiteSpace(venue) ? Venue : venue.Trim();
    }

    public void Postpone()
    {
        if (Status == MatchStatus.Played)
            throw new DomainException(ErrorCode.MatchPostponePlayed);

        Status = MatchStatus.Postponed;
    }

    public void Cancel()
    {
        if (Status == MatchStatus.Played)
            throw new DomainException(ErrorCode.MatchCancelPlayed);

        Status = MatchStatus.Cancelled;
        _homeGoals = null;
        _awayGoals = null;
    }

    public void MarkInProgress()
    {
        if (Status is MatchStatus.Played or MatchStatus.Cancelled)
            throw new DomainException(ErrorCode.MatchCannotStart);

        Status = MatchStatus.InProgress;
    }

    /// <summary>Vincula el partido con su acta en la federación.</summary>
    public void LinkOfficial(ExternalRef officialRef) => OfficialRef = officialRef;

    public bool Involves(Guid teamId) => HomeTeamId == teamId || AwayTeamId == teamId;

    public bool IsHome(Guid teamId) => HomeTeamId == teamId;

    public Guid OpponentOf(Guid teamId)
    {
        if (teamId == HomeTeamId) return AwayTeamId;
        if (teamId == AwayTeamId) return HomeTeamId;
        throw new DomainException(ErrorCode.MatchTeamNotInvolved);
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
