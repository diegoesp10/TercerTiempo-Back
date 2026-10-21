using TercerTiempo.Domain.Coaches;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.Performances;
using TercerTiempo.Domain.Players;
using TercerTiempo.Domain.Squads;

namespace TercerTiempo.Domain.Tests;

internal static class TestData
{
    public static readonly DateTimeOffset Now = new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);
    public static readonly Guid SeasonId = Guid.NewGuid();
    public static readonly Guid HomeTeamId = Guid.NewGuid();
    public static readonly Guid AwayTeamId = Guid.NewGuid();

    public static Player NewPlayer(string name = "Pelayo Fernández", Position position = Position.Forward) =>
        Player.Register(Guid.NewGuid(), name, position, new DateOnly(1998, 4, 12), Now);

    public static Coach NewCoach(string name = "Míster González") =>
        Coach.Register(Guid.NewGuid(), name, Now);

    /// <summary>Plantilla (del equipo local por defecto) con un primer entrenador recién creado.</summary>
    public static (Squad Squad, Guid CoachId) NewSquad(Guid? teamId = null)
    {
        var coachId = Guid.NewGuid();
        return (Squad.Create(teamId ?? HomeTeamId, SeasonId, coachId, Now), coachId);
    }

    public static Match PlayedMatch(int home = 2, int away = 1)
    {
        var match = Match.Schedule(SeasonId, 1, HomeTeamId, AwayTeamId, Now.AddDays(-3), "Campo municipal", null);
        match.RecordResult(home, away, Now.AddDays(-3).AddHours(2));
        return match;
    }

    public static Match ScheduledMatch() =>
        Match.Schedule(SeasonId, 2, HomeTeamId, AwayTeamId, Now.AddDays(4), null, null);

    public static PerformanceStats ValidStats(int minutes = 90, int goals = 1, int assists = 0) =>
        new(Started: true, MinutesPlayed: minutes, Goals: goals, Assists: assists, Shots: 3, ShotsOnTarget: 2,
            KeyPasses: 1, Tackles: 2, Interceptions: 1, Saves: 0, YellowCards: 0, RedCard: false,
            Notes: null);
}
