using TercerTiempo.Domain.Matches;

namespace TercerTiempo.Domain.Squads;

public enum SuspensionCause
{
    YellowCards = 1,
    SecondYellow = 2,
    RedCard = 3,
}

// Las tarjetas de un jugador en un partido, sacadas del acta que mande en ese partido.
public sealed record MatchCards(int YellowCards, bool RedCard)
{
    // Expulsado por doble amarilla: se sanciona la expulsión y esas dos amarillas no entran en el ciclo.
    public bool SecondYellow => RedCard && YellowCards >= 2;

    public int CycleYellows => SecondYellow ? 0 : Math.Max(0, YellowCards);
}

public sealed record Suspension(Guid SquadPlayerId, Guid MatchId, SuspensionCause Cause, Guid FromMatchId, int FromMatchday, int Yellows);

// Quién no puede jugar cada partido de liga por sanción, como manda el reglamento de competición: cada cinco amarillas
// (la 5.ª, la 10.ª…) y cada expulsión cuestan un partido, el siguiente que juegue el equipo en esa competición. Si se
// juntan varias, se cumplen por orden, una por partido; los partidos aplazados o cancelados no cuentan. Se calcula al leer
// con las tarjetas de las actas, así que corregir una tarjeta corrige la sanción. Una roja directa puede costar más
// partidos si lo decide el comité de competición: aquí solo cuenta el mínimo.
public sealed class SuspensionCalendar
{
    public const int YellowsPerSuspension = 5;

    private readonly Dictionary<(Guid Player, Guid Match), Suspension> _suspensions;
    private readonly Dictionary<(Guid Player, Guid Match), int> _yellowsBefore;

    public static readonly SuspensionCalendar Empty = new([], []);

    private SuspensionCalendar(Dictionary<(Guid, Guid), Suspension> suspensions, Dictionary<(Guid, Guid), int> yellowsBefore)
    {
        _suspensions = suspensions;
        _yellowsBefore = yellowsBefore;
    }

    public Suspension? On(Guid squadPlayerId, Guid matchId) => _suspensions.GetValueOrDefault((squadPlayerId, matchId));

    public IReadOnlyList<Suspension> For(Guid matchId) => _suspensions.Values.Where(s => s.MatchId == matchId).ToList();

    // Amarillas del ciclo que lleva el jugador al empezar ese partido (de 0 a 4): con 4, la siguiente le cuesta un partido.
    public int YellowsBefore(Guid squadPlayerId, Guid matchId) => _yellowsBefore.GetValueOrDefault((squadPlayerId, matchId));

    public static SuspensionCalendar Build(IEnumerable<Match> matches, IReadOnlyDictionary<Guid, IReadOnlyDictionary<Guid, MatchCards>> cardsByMatch)
    {
        var ordered = matches
            .Where(m => m.Status is not (MatchStatus.Cancelled or MatchStatus.Postponed))
            .OrderBy(m => m.KickoffAt)
            .ThenBy(m => m.Matchday)
            .ToList();
        var pending = new Dictionary<Guid, Queue<Suspension>>();
        var yellows = new Dictionary<Guid, int>();
        var suspensions = new Dictionary<(Guid, Guid), Suspension>();
        var yellowsBefore = new Dictionary<(Guid, Guid), int>();

        foreach (var match in ordered)
        {
            foreach (var (player, total) in yellows)
                if (total % YellowsPerSuspension is var inCycle and > 0)
                    yellowsBefore[(player, match.Id)] = inCycle;

            // Primero se cumple lo pendiente: cada partido del equipo cuenta, juegue o no el sancionado.
            foreach (var (player, queue) in pending)
                if (queue.TryDequeue(out var due))
                    suspensions[(player, match.Id)] = due with { MatchId = match.Id };

            if (!cardsByMatch.TryGetValue(match.Id, out var cards)) continue;
            foreach (var (player, card) in cards)
            {
                if (card.RedCard)
                    Enqueue(player, card.SecondYellow ? SuspensionCause.SecondYellow : SuspensionCause.RedCard, match, 0);
                for (var i = 0; i < card.CycleYellows; i++)
                {
                    var total = yellows[player] = yellows.GetValueOrDefault(player) + 1;
                    if (total % YellowsPerSuspension == 0)
                        Enqueue(player, SuspensionCause.YellowCards, match, total);
                }
            }
        }

        return new SuspensionCalendar(suspensions, yellowsBefore);

        void Enqueue(Guid player, SuspensionCause cause, Match from, int yellowCount)
        {
            if (!pending.TryGetValue(player, out var queue))
                pending[player] = queue = new Queue<Suspension>();
            queue.Enqueue(new Suspension(player, Guid.Empty, cause, from.Id, from.Matchday, yellowCount));
        }
    }
}
