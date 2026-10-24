using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Common.Errors;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.Squads;

namespace TercerTiempo.Domain.Performances;

// Datos colectivos del partido que no trae ningún acta pública (esta solo recoge alineaciones, cambios, goles y
// tarjetas): los apunta el cuerpo técnico, desde el punto de vista de su equipo («a favor» y «en contra»).
public sealed record TeamMatchStatsData(
    int? PossessionPct,
    int CornersFor,
    int CornersAgainst,
    int ShotsFor,
    int ShotsAgainst,
    int ShotsOnTargetFor,
    int ShotsOnTargetAgainst,
    int OffsidesFor,
    int OffsidesAgainst,
    int FoulsCommitted,
    int FoulsReceived,
    int SetPieceGoalsFor,
    int SetPieceGoalsAgainst,
    string? Notes)
{
    public const int MaxNotesLength = 2000;

    public void EnsureValid(int? goalsFor, int? goalsAgainst)
    {
        if (PossessionPct is { } possession) Guard.InRange(possession, 0, 100, "posesión");
        Guard.NotNegative(CornersFor, "córners a favor");
        Guard.NotNegative(CornersAgainst, "córners en contra");
        Guard.NotNegative(ShotsFor, "tiros a favor");
        Guard.NotNegative(ShotsAgainst, "tiros en contra");
        Guard.NotNegative(ShotsOnTargetFor, "tiros a puerta a favor");
        Guard.NotNegative(ShotsOnTargetAgainst, "tiros a puerta en contra");
        Guard.NotNegative(OffsidesFor, "fueras de juego propios");
        Guard.NotNegative(OffsidesAgainst, "fueras de juego del rival");
        Guard.NotNegative(FoulsCommitted, "faltas cometidas");
        Guard.NotNegative(FoulsReceived, "faltas recibidas");
        Guard.NotNegative(SetPieceGoalsFor, "goles a balón parado a favor");
        Guard.NotNegative(SetPieceGoalsAgainst, "goles a balón parado en contra");

        if (ShotsOnTargetFor > ShotsFor || ShotsOnTargetAgainst > ShotsAgainst)
            throw new DomainException(ErrorCode.SheetShotsOnTarget);

        if (goalsFor is { } scored && SetPieceGoalsFor > scored || goalsAgainst is { } conceded && SetPieceGoalsAgainst > conceded)
            throw new DomainException(ErrorCode.TeamStatsSetPieceGoals);

        if (Notes is { Length: > MaxNotesLength })
            throw new DomainException(ErrorCode.SheetNotesTooLong, MaxNotesLength);
    }
}

public sealed class TeamMatchStats : AggregateRoot
{
    public Guid SquadId { get; private set; }
    public Guid MatchId { get; private set; }

    public int? PossessionPct { get; private set; }
    public int CornersFor { get; private set; }
    public int CornersAgainst { get; private set; }
    public int ShotsFor { get; private set; }
    public int ShotsAgainst { get; private set; }
    public int ShotsOnTargetFor { get; private set; }
    public int ShotsOnTargetAgainst { get; private set; }
    public int OffsidesFor { get; private set; }
    public int OffsidesAgainst { get; private set; }
    public int FoulsCommitted { get; private set; }
    public int FoulsReceived { get; private set; }
    public int SetPieceGoalsFor { get; private set; }
    public int SetPieceGoalsAgainst { get; private set; }
    public string? Notes { get; private set; }

    public Guid RecordedByCoachId { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private TeamMatchStats() { }

    public static TeamMatchStats Record(Squad squad, Match match, Guid coachId, TeamMatchStatsData data, DateTimeOffset now)
    {
        var stats = new TeamMatchStats { Id = NewId(), SquadId = squad.Id, MatchId = match.Id };
        stats.Update(squad, match, coachId, data, now);
        return stats;
    }

    public void Update(Squad squad, Match match, Guid coachId, TeamMatchStatsData data, DateTimeOffset now)
    {
        if (squad.Id != SquadId || match.Id != MatchId)
            throw new DomainException(ErrorCode.SheetLineMismatch);

        PerformancePolicy.EnsureCanRecord(squad, coachId, match, Squad.Today(now));
        data.EnsureValid(match.GoalsFor(squad.TeamId), match.GoalsAgainst(squad.TeamId));

        PossessionPct = data.PossessionPct;
        CornersFor = data.CornersFor;
        CornersAgainst = data.CornersAgainst;
        ShotsFor = data.ShotsFor;
        ShotsAgainst = data.ShotsAgainst;
        ShotsOnTargetFor = data.ShotsOnTargetFor;
        ShotsOnTargetAgainst = data.ShotsOnTargetAgainst;
        OffsidesFor = data.OffsidesFor;
        OffsidesAgainst = data.OffsidesAgainst;
        FoulsCommitted = data.FoulsCommitted;
        FoulsReceived = data.FoulsReceived;
        SetPieceGoalsFor = data.SetPieceGoalsFor;
        SetPieceGoalsAgainst = data.SetPieceGoalsAgainst;
        Notes = string.IsNullOrWhiteSpace(data.Notes) ? null : data.Notes.Trim();
        RecordedByCoachId = Guard.NotEmpty(coachId, nameof(coachId));
        UpdatedAt = now;
    }
}

public interface ITeamMatchStatsStore
{
    Task<TeamMatchStats?> GetAsync(Guid squadId, Guid matchId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TeamMatchStats>> GetBySquadAsync(Guid squadId, CancellationToken cancellationToken = default);
    void Add(TeamMatchStats stats);
}
