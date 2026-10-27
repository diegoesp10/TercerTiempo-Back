using System.ComponentModel;
using TercerTiempo.Application.Abstractions.FootballData;
using TercerTiempo.Application.Abstractions.Messaging;
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
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<SyncLeagueZonesCommand, LeagueZonesSynced>
{
    public async Task<LeagueZonesSynced> Handle(SyncLeagueZonesCommand command, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var leagues = (await competitions.GetAllAsync(cancellationToken))
            .Where(c => c.ExternalRef?.Provider == provider.ProviderName && c.CurrentSeason is not null)
            .ToList();

        var (updated, failed) = (0, 0);
        foreach (var competition in leagues)
        {
            var published = await provider.GetLeagueZonesAsync(competition.ExternalRef!.ExternalId, cancellationToken);
            if (published is null)
            {
                failed++;
                continue;
            }

            var season = competition.CurrentSeason!;
            var stored = (await zones.GetBySeasonAsync(season.Id, cancellationToken)).ToDictionary(z => z.Position);
            var wanted = published.GroupBy(z => z.Position).Select(g => g.First()).ToDictionary(z => z.Position);
            if (stored.Count == wanted.Count && stored.Values.All(z => wanted.TryGetValue(z.Position, out var data) && z.Matches(data)))
                continue;

            foreach (var zone in stored.Values.Where(z => !wanted.ContainsKey(z.Position))) zones.Remove(zone);
            foreach (var data in wanted.Values)
            {
                if (stored.TryGetValue(data.Position, out var zone)) zone.Update(data, now);
                else zones.Add(SeasonZone.Create(season.Id, data, now));
            }
            updated++;
        }

        if (updated > 0) await unitOfWork.SaveChangesAsync(cancellationToken);
        return new LeagueZonesSynced(leagues.Count, updated, failed);
    }
}
