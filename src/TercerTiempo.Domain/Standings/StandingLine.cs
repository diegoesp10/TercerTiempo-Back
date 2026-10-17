namespace TercerTiempo.Domain.Standings;

public sealed record StandingLine(
    int Position, int Played, int Won, int Drawn, int Lost, int GoalsFor, int GoalsAgainst, int Points);
