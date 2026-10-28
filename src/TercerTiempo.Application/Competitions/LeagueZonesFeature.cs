using System.ComponentModel;
using TercerTiempo.Application.Abstractions.FootballData;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Jobs;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Competitions;

namespace TercerTiempo.Application.Competitions;

public sealed record LeagueZoneDto(
    int Position,
    [property: Description("Champion, DirectPromotion, PromotionPlayoff, RelegationPlayoff, Relegation u Other.")]
    LeagueZoneKind Kind,
    [property: Description("Como lo llama la web: «Ascenso directo», «Promoción de ascenso», «Descenso»...")]
    string Label,
    [property: Description("Color #rrggbb con el que la web marca el puesto.")]
    string Color)
{
    public static LeagueZoneDto From(SeasonZone zone) => new(zone.Position, zone.Kind, zone.Label, zone.Color);
}

public sealed record SyncLeagueZonesCommand : ICommand<LeagueZonesSynced>;

public sealed record LeagueZonesSynced(int Leagues, int Updated, int Failed);

// Plazas de la clasificación de cada liga del proveedor en su temporada en curso (una página por liga). Solo se escribe si
// algo ha cambiado; si la web no responde, se queda lo que había.
internal sealed class SyncLeagueZonesHandler(
    IFootballDataProvider provider,
    ICompetitionStore competitions,
    ISeasonZoneStore zones,
    IJobProgress progress,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<SyncLeagueZonesCommand, LeagueZonesSynced>
{
    private const string Job = JobNames.LeagueZones;

    public async Task<LeagueZonesSynced> Handle(SyncLeagueZonesCommand command, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var leagues = (await competitions.GetAllAsync(cancellationToken))
            .Where(c => c.ExternalRef?.Provider == provider.ProviderName && c.CurrentSeason is not null)
            .ToList();

        var (updated, failed, done) = (0, 0, 0);
        foreach (var competition in leagues)
        {
            progress.Step(Job, competition.Name, done++, leagues.Count);
            var published = await provider.GetLeagueZonesAsync(competition.ExternalRef!.ExternalId, cancellationToken);
            if (published is null)
            {
                failed++;
                progress.Event(Job, $"{competition.Name}: no se pudo leer su clasificación; se quedan las plazas que había", JobEventTone.Warning);
                continue;
            }

            var season = competition.CurrentSeason!;
            var stored = (await zones.GetBySeasonAsync(season.Id, cancellationToken)).ToDictionary(z => z.Position);
            var wanted = published.GroupBy(z => z.Position).Select(g => g.First()).ToDictionary(z => z.Position);
            if (stored.Count == wanted.Count && stored.Values.All(z => wanted.TryGetValue(z.Position, out var data) && z.Matches(data)))
            {
                progress.Event(Job, $"{competition.Name}: sin cambios · {Describe(wanted.Values)}");
                continue;
            }

            foreach (var zone in stored.Values.Where(z => !wanted.ContainsKey(z.Position))) zones.Remove(zone);
            foreach (var data in wanted.Values)
            {
                if (stored.TryGetValue(data.Position, out var zone)) zone.Update(data, now);
                else zones.Add(SeasonZone.Create(season.Id, data, now));
            }
            updated++;
            progress.Event(Job, $"{competition.Name}: {(stored.Count == 0 ? "plazas nuevas" : "plazas cambiadas")} · {Describe(wanted.Values)}", JobEventTone.Success);
        }

        if (updated > 0) await unitOfWork.SaveChangesAsync(cancellationToken);
        progress.Step(Job, null, leagues.Count, leagues.Count);
        return new LeagueZonesSynced(leagues.Count, updated, failed);
    }

    // «Ascenso directo 1.º-3.º · Descenso 15.º-20.º», en el orden de la clasificación.
    private static string Describe(IEnumerable<LeagueZoneData> zones)
    {
        var parts = zones
            .OrderBy(z => z.Position)
            .GroupBy(z => z.Label)
            .Select(g =>
            {
                var (first, last) = (g.Min(z => z.Position), g.Max(z => z.Position));
                return first == last ? $"{g.Key} {first}.º" : $"{g.Key} {first}.º-{last}.º";
            })
            .ToList();
        return parts.Count > 0 ? string.Join(" · ", parts) : "la web no marca ningún puesto";
    }
}
