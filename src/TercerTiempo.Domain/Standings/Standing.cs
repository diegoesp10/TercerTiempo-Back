using TercerTiempo.Domain.Common;

namespace TercerTiempo.Domain.Standings;

public sealed class Standing : AggregateRoot
{
    public Guid SeasonId { get; private set; }
    public Guid TeamId { get; private set; }
    public int Position { get; private set; }
    public int Played { get; private set; }
    public int Won { get; private set; }
    public int Drawn { get; private set; }
    public int Lost { get; private set; }
    public int GoalsFor { get; private set; }
    public int GoalsAgainst { get; private set; }
    public int Points { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public int GoalDifference => GoalsFor - GoalsAgainst;

    private Standing() { }

    public static Standing Create(Guid seasonId, Guid teamId, StandingLine line, DateTimeOffset now)
    {
        var standing = new Standing
        {
            Id = NewId(),
            SeasonId = Guard.NotEmpty(seasonId, nameof(seasonId)),
            TeamId = Guard.NotEmpty(teamId, nameof(teamId)),
        };
        standing.Update(line, now);
        return standing;
    }

    public void Update(StandingLine line, DateTimeOffset now)
    {
        Position = Guard.InRange(line.Position, 1, 100, "posición");
        Played = Guard.NotNegative(line.Played, "jugados");
        Won = Guard.NotNegative(line.Won, "ganados");
        Drawn = Guard.NotNegative(line.Drawn, "empatados");
        Lost = Guard.NotNegative(line.Lost, "perdidos");
        GoalsFor = Guard.NotNegative(line.GoalsFor, "goles a favor");
        GoalsAgainst = Guard.NotNegative(line.GoalsAgainst, "goles en contra");
        Points = Guard.NotNegative(line.Points, "puntos");
        UpdatedAt = now;
    }
}
