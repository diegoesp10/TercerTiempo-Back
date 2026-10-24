using System.Globalization;
using TercerTiempo.Application.Abstractions.FootballData;

namespace TercerTiempo.Infrastructure.FootballData.FutbolAsturiano;

internal static class HiddenEventPlanner
{
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

    private static List<int> PrioritizeUnknown(List<int> unknown, IReadOnlyCollection<VisibleEvent> visibleEvents, int first, DateTimeOffset now)
    {
        var played = visibleEvents.Where(e => e.KickoffAt < now).Select(e => e.Id).ToList();
        var frontier = played.Count > 0 ? played.Max() : first - 1;
        var parity = DominantParity(visibleEvents.Select(e => e.Id).ToList());

        bool Next(int id) => id > frontier && (parity is null || Math.Abs(id % 2) == parity);

        return unknown.Where(Next).Concat(unknown.Where(id => !Next(id))).ToList();
    }

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

internal readonly record struct VisibleEvent(int Id, DateTimeOffset KickoffAt);

internal sealed record HiddenEventPlan(
    int Hidden,
    IReadOnlyList<MatchSnapshot> Reused,
    IReadOnlyList<int> Refresh,
    IReadOnlyList<int> Unknown,
    int NextId)
{
    public static readonly HiddenEventPlan None = new(0, [], [], [], 0);
}
