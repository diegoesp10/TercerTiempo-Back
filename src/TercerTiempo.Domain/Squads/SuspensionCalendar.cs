using TercerTiempo.Domain.Matches;

namespace TercerTiempo.Domain.Squads;

public enum SuspensionCause
{
    YellowCards = 1,
    SecondYellow = 2,
    RedCard = 3,

    // Lo que publica el comité de competición: cuántos partidos y por qué. A un jugador que nombra le cuenta lo suyo en
    // vez de lo de sus tarjetas en ese partido, también en las amarillas (la acumulación la sanciona él).
    Committee = 4,
}

// Las tarjetas de un jugador en un partido, sacadas del acta que mande en ese partido.
public sealed record MatchCards(int YellowCards, bool RedCard)
{
    // Expulsado por doble amarilla: se sanciona la expulsión y esas dos amarillas no entran en el ciclo.
    public bool SecondYellow => RedCard && YellowCards >= 2;

    public int CycleYellows => SecondYellow ? 0 : Math.Max(0, YellowCards);
}

// Matches y Nth: de cuántos partidos es la sanción y cuál de ellos se cumple en este (las de las tarjetas son de uno).
// Detail y DecidedOn: el motivo que da el comité y el día de su reunión.
public sealed record Suspension(
    Guid SquadPlayerId, Guid MatchId, SuspensionCause Cause, Guid FromMatchId, int FromMatchday, int Yellows,
    int Matches = 1, int Nth = 1, string? Detail = null, DateOnly? DecidedOn = null);

// Partidos de suspensión que pone el comité a un jugador por lo que pasó en un partido.
public sealed record CommitteePenalty(int Matches, string Reason, DateOnly DecidedOn);

// Lo que decidió el comité sobre un partido ya visto: las sanciones de cada jugador, por orden. A un jugador que no sale
// le cuentan sus tarjetas: no se quita una sanción por no encontrarla (un nombre que no cruza, un castigo por días que
// no se lee), porque alinear a un sancionado cuesta el partido.
public sealed record CommitteeDecision(IReadOnlyDictionary<Guid, IReadOnlyList<CommitteePenalty>> ByPlayer)
{
    public static readonly CommitteeDecision Nothing = new(new Dictionary<Guid, IReadOnlyList<CommitteePenalty>>());
}

// Quién no puede jugar cada partido de liga por sanción, como manda el reglamento de competición: cada cinco amarillas
// (la 5.ª, la 10.ª…) y cada expulsión cuestan un partido, el siguiente que juegue el equipo en esa competición. Si se
// juntan varias, se cumplen por orden, una por partido; los partidos aplazados o cancelados no cuentan. Se calcula al leer
// con las tarjetas de las actas, así que corregir una tarjeta corrige la sanción. Cuando el comité de competición publica
// lo que pone a un jugador por un partido, eso manda: los partidos que cuesta cada expulsión (una roja directa puede
// costar varios) y las suspensiones por acumulación; mientras no lo publica, o si no le nombra, cuenta el mínimo de las
// tarjetas.
public sealed class SuspensionCalendar
{
    public const int YellowsPerSuspension = 5;

    private readonly Dictionary<(Guid Player, Guid Match), Suspension> _suspensions;
    private readonly Dictionary<(Guid Player, Guid Match), int> _yellowsBefore;

    public static readonly SuspensionCalendar Empty = new([], [], new Dictionary<Guid, CommitteeDecision>());

    private SuspensionCalendar(
        Dictionary<(Guid, Guid), Suspension> suspensions, Dictionary<(Guid, Guid), int> yellowsBefore, IReadOnlyDictionary<Guid, CommitteeDecision> decisions)
    {
        _suspensions = suspensions;
        _yellowsBefore = yellowsBefore;
        Decisions = decisions;
    }

    // Lo que ha decidido el comité en cada partido que ya ha visto (por partido).
    public IReadOnlyDictionary<Guid, CommitteeDecision> Decisions { get; }

    public Suspension? On(Guid squadPlayerId, Guid matchId) => _suspensions.GetValueOrDefault((squadPlayerId, matchId));

    public IReadOnlyList<Suspension> For(Guid matchId) => _suspensions.Values.Where(s => s.MatchId == matchId).ToList();

    // Todas las que caen en algún partido de la temporada.
    public IReadOnlyCollection<Suspension> All => _suspensions.Values;

    // Amarillas del ciclo que lleva el jugador al empezar ese partido (de 0 a 4): con 4, la siguiente le cuesta un partido.
    public int YellowsBefore(Guid squadPlayerId, Guid matchId) => _yellowsBefore.GetValueOrDefault((squadPlayerId, matchId));

    public static SuspensionCalendar Build(
        IEnumerable<Match> matches,
        IReadOnlyDictionary<Guid, IReadOnlyDictionary<Guid, MatchCards>> cardsByMatch,
        IReadOnlyDictionary<Guid, CommitteeDecision>? committee = null)
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

            // Las amarillas cuentan siempre para el ciclo; la sanción que traen, solo si el comité no ha nombrado al jugador.
            var decision = committee?.GetValueOrDefault(match.Id);
            if (cardsByMatch.TryGetValue(match.Id, out var cards))
            {
                foreach (var (player, card) in cards)
                {
                    var ruled = decision?.ByPlayer.ContainsKey(player) == true;
                    if (card.RedCard && !ruled)
                        Enqueue(player, new Suspension(player, Guid.Empty, card.SecondYellow ? SuspensionCause.SecondYellow : SuspensionCause.RedCard, match.Id, match.Matchday, 0));
                    for (var i = 0; i < card.CycleYellows; i++)
                    {
                        var total = yellows[player] = yellows.GetValueOrDefault(player) + 1;
                        if (total % YellowsPerSuspension == 0 && !ruled)
                            Enqueue(player, new Suspension(player, Guid.Empty, SuspensionCause.YellowCards, match.Id, match.Matchday, total));
                    }
                }
            }

            if (decision is null) continue;
            foreach (var (player, penalties) in decision.ByPlayer)
                foreach (var penalty in penalties)
                    for (var nth = 1; nth <= penalty.Matches; nth++)
                        Enqueue(player, new Suspension(
                            player, Guid.Empty, SuspensionCause.Committee, match.Id, match.Matchday, 0, penalty.Matches, nth, penalty.Reason, penalty.DecidedOn));
        }

        return new SuspensionCalendar(suspensions, yellowsBefore, committee ?? new Dictionary<Guid, CommitteeDecision>());

        void Enqueue(Guid player, Suspension suspension)
        {
            if (!pending.TryGetValue(player, out var queue))
                pending[player] = queue = new Queue<Suspension>();
            queue.Enqueue(suspension);
        }
    }
}
