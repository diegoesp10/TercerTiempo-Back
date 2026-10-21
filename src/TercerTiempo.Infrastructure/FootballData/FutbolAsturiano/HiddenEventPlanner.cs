using System.Globalization;
using TercerTiempo.Application.Abstractions.FootballData;

namespace TercerTiempo.Infrastructure.FootballData.FutbolAsturiano;

/// <summary>
/// Decide qué partidos ocultos por la API hay que leer de su página pública en una sincronización.
///
/// La API REST de WordPress solo devuelve los partidos publicados, pero su cabecera <c>X-WP-Total</c> los cuenta todos,
/// así que se sabe cuántos faltan. El calendario de cada liga se generó de una vez, con identificadores consecutivos:
/// los ocultos son los huecos del bloque <c>[primer id visible, primer id + total)</c>.
///
/// Orden de lectura, para llegar antes a las jornadas próximas: primero los ids posteriores a la "frontera" (el mayor
/// id visible ya disputado), porque el calendario se creó jornada a jornada. Algunas ligas (3ª AsturFútbol) intercalan
/// ida y vuelta (ids impares la ida, pares la vuelta); se detecta porque casi todos los visibles comparten paridad, y
/// entonces se sigue primero esa serie. Después, el resto de huecos de menor a mayor.
///
/// Para no castigar al sitio (cada página tarda segundos) los ya conocidos se reutilizan, solo se vuelven a leer los
/// que se juegan pronto (pueden cambiar de hora o tener ya resultado) y las lecturas nuevas tienen un tope por liga:
/// el resto llega en las siguientes sincronizaciones.
/// </summary>
internal static class HiddenEventPlanner
{
    /// <summary>Proporción mínima de ids visibles con la misma paridad para considerar ida y vuelta intercaladas.</summary>
    private const double InterleavedThreshold = 0.9;
    private const int InterleavedMinSample = 6;

    public static HiddenEventPlan Plan(
        IReadOnlyCollection<VisibleEvent> visibleEvents,
        int total,
        IReadOnlyCollection<MatchSnapshot> known,
        DateTimeOffset now,
        TimeSpan refreshAhead,
        TimeSpan refreshBehind)
    {
        var visible = visibleEvents.Select(e => e.Id).ToHashSet();
        var hidden = total - visible.Count;
        if (hidden <= 0) return HiddenEventPlan.None;

        var knownById = known
            .Select(k => (Id: ParseId(k.ExternalId), Snapshot: k))
            .Where(k => k.Id is > 0 && !visible.Contains(k.Id.Value))
            .GroupBy(k => k.Id!.Value)
            .ToDictionary(g => g.Key, g => g.First().Snapshot);

        // Sin partidos visibles el bloque se ancla en los ya conocidos; sin ninguno de los dos no hay por dónde empezar.
        var anchors = visible.Count > 0 ? visible : knownById.Keys.ToHashSet();
        if (anchors.Count == 0) return new HiddenEventPlan(hidden, [], [], [], 0);

        var first = anchors.Min();
        var last = Math.Max(anchors.Max(), first + total - 1);

        var reused = new List<MatchSnapshot>();
        var refresh = new List<(int Id, DateTimeOffset Kickoff)>();
        var unknown = new List<int>();

        for (var id = first; id <= last; id++)
        {
            if (visible.Contains(id)) continue;

            if (!knownById.TryGetValue(id, out var snapshot))
            {
                unknown.Add(id);
                continue;
            }

            var dueSoon = snapshot.KickoffAt <= now + refreshAhead && snapshot.KickoffAt >= now - refreshBehind;
            if (dueSoon)
                refresh.Add((id, snapshot.KickoffAt));
            else
                reused.Add(snapshot);
        }

        return new HiddenEventPlan(
            hidden,
            reused,
            refresh.OrderBy(r => r.Kickoff).Select(r => r.Id).ToList(),
            PrioritizeUnknown(unknown, visibleEvents, first, now),
            NextId: last + 1);
    }

    /// <summary>Primero la serie que continúa tras la frontera (jornadas próximas); luego el resto, de menor a mayor.</summary>
    private static List<int> PrioritizeUnknown(List<int> unknown, IReadOnlyCollection<VisibleEvent> visibleEvents, int first, DateTimeOffset now)
    {
        var played = visibleEvents.Where(e => e.KickoffAt < now).Select(e => e.Id).ToList();
        var frontier = played.Count > 0 ? played.Max() : first - 1;
        var parity = DominantParity(visibleEvents.Select(e => e.Id).ToList());

        bool Next(int id) => id > frontier && (parity is null || Math.Abs(id % 2) == parity);

        return unknown.Where(Next).Concat(unknown.Where(id => !Next(id))).ToList();
    }

    /// <summary>Paridad común a casi todos los ids visibles (ida y vuelta intercaladas) o null si no la hay.</summary>
    private static int? DominantParity(IReadOnlyList<int> ids)
    {
        if (ids.Count < InterleavedMinSample) return null;

        var odd = ids.Count(id => Math.Abs(id % 2) == 1);
        if (odd >= ids.Count * InterleavedThreshold) return 1;
        if (ids.Count - odd >= ids.Count * InterleavedThreshold) return 0;
        return null;
    }

    private static int? ParseId(string externalId) =>
        int.TryParse(externalId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : null;
}

/// <summary>Partido devuelto por la API: su id y su fecha (para saber cuáles ya se jugaron).</summary>
internal readonly record struct VisibleEvent(int Id, DateTimeOffset KickoffAt);

/// <param name="Hidden">Partidos que la API no devuelve (total − visibles).</param>
/// <param name="Reused">Ya conocidos y lejanos en el tiempo: se reutilizan sin descargar nada.</param>
/// <param name="Refresh">Ya conocidos que se juegan pronto: se vuelven a leer (hora definitiva, resultado). Por fecha.</param>
/// <param name="Unknown">Identificadores del bloque aún no leídos, en orden de prioridad.</param>
/// <param name="NextId">Primer identificador tras el bloque, por si alguno del bloque resulta no ser de la liga.</param>
internal sealed record HiddenEventPlan(
    int Hidden,
    IReadOnlyList<MatchSnapshot> Reused,
    IReadOnlyList<int> Refresh,
    IReadOnlyList<int> Unknown,
    int NextId)
{
    public static readonly HiddenEventPlan None = new(0, [], [], [], 0);
}
