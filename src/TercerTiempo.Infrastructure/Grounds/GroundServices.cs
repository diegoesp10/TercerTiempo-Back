using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Grounds;
using TercerTiempo.Application.Jobs;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Grounds;
using TercerTiempo.Infrastructure.Jobs;
using TercerTiempo.Infrastructure.Persistence;
using TercerTiempo.Infrastructure.Persistence.Configurations;
using TercerTiempo.Infrastructure.RealData;

namespace TercerTiempo.Infrastructure.Grounds;

public sealed class GroundOptions
{
    public const string SectionName = "Grounds";

    public bool Enabled { get; set; } = true;
    // Nominatim, el buscador de OpenStreetMap: datos abiertos (ODbL) con un máximo de una petición por segundo.
    public string BaseUrl { get; set; } = "https://nominatim.openstreetmap.org/";
    public string UserAgent { get; set; } = "TercerTiempo/1.0 (+https://github.com/diegoesp10/TercerTiempo-Back)";
    // Asturias: oeste, norte, este, sur.
    public string ViewBox { get; set; } = "-7.25,43.75,-4.45,42.85";
    public int DelayMilliseconds { get; set; } = 1100;
}

internal sealed class GroundConfiguration : IEntityTypeConfiguration<Ground>
{
    public void Configure(EntityTypeBuilder<Ground> builder)
    {
        builder.ToTable("Grounds");
        builder.ConfigureAggregate();
        builder.Ignore(g => g.IsLocated);
        builder.Ignore(g => g.MapsUrl);

        builder.Property(g => g.Name).HasMaxLength(Ground.MaxNameLength).IsRequired();
        builder.Property(g => g.Key).HasMaxLength(Ground.MaxNameLength).IsRequired();
        builder.Property(g => g.Address).HasMaxLength(Ground.MaxAddressLength);
        builder.Property(g => g.Locality).HasMaxLength(Ground.MaxLocalityLength);
        builder.Property(g => g.Latitude).HasPrecision(9, 6);
        builder.Property(g => g.Longitude).HasPrecision(9, 6);

        builder.HasIndex(g => g.Name).IsUnique();
        builder.HasIndex(g => g.Key);
    }
}

internal sealed class GroundStore(AppDbContext db) : IGroundStore
{
    public Task<Ground?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.Set<Ground>().FirstOrDefaultAsync(g => g.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Ground>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default)
    {
        var list = ids.Distinct().ToList();
        return list.Count == 0 ? [] : await db.Set<Ground>().Where(g => list.Contains(g.Id)).ToListAsync(cancellationToken);
    }

    public Task<Ground?> GetByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        var clean = name.Trim();
        return db.Set<Ground>().FirstOrDefaultAsync(g => g.Name == clean, cancellationToken);
    }

    // El nombre del campo de cada partido se compara como lo compara SQL Server (sin mayúsculas ni espacios finales), igual
    // que el índice único del catálogo.
    public async Task<IReadOnlyList<SeasonGround>> GetBySeasonAsync(Guid seasonId, CancellationToken cancellationToken = default)
    {
        var rows = await db.Matches.AsNoTracking()
            .Where(m => m.SeasonId == seasonId && m.Venue != null && m.Venue != "")
            .GroupBy(m => new { m.Venue, m.HomeTeamId })
            .Select(g => new { g.Key.Venue, g.Key.HomeTeamId, Count = g.Count() })
            .ToListAsync(cancellationToken);
        if (rows.Count == 0) return [];

        var names = rows.Select(r => r.Venue!.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var grounds = await db.Set<Ground>().AsNoTracking().Where(g => names.Contains(g.Name)).ToListAsync(cancellationToken);
        var byName = grounds.ToDictionary(g => g.Name.Trim(), StringComparer.OrdinalIgnoreCase);

        return rows
            .Where(r => byName.ContainsKey(r.Venue!.Trim()))
            .GroupBy(r => byName[r.Venue!.Trim()].Id)
            .Select(g => new SeasonGround(
                grounds.First(x => x.Id == g.Key),
                g.GroupBy(r => r.HomeTeamId).ToDictionary(t => t.Key, t => t.Sum(r => r.Count))))
            .ToList();
    }

    public async Task<IReadOnlyList<string>> GetUncataloguedVenuesAsync(CancellationToken cancellationToken = default) =>
        await db.Matches.AsNoTracking()
            .Where(m => m.Venue != null && m.Venue != "" && !db.Set<Ground>().Any(g => g.Name == m.Venue))
            .Select(m => m.Venue!)
            .Distinct()
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Ground>> GetToLocateAsync(DateTimeOffset now, int take, CancellationToken cancellationToken = default)
    {
        var retryBefore = now - Ground.RetryLocateAfter;
        return await db.Set<Ground>()
            .Where(g => g.Latitude == null && g.LocateAttempts < Ground.MaxLocateAttempts && (g.LocatedAt == null || g.LocatedAt <= retryBefore))
            .OrderBy(g => g.LocatedAt == null ? 0 : 1).ThenBy(g => g.LocatedAt).ThenBy(g => g.Name)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    public void Add(Ground ground) => db.Set<Ground>().Add(ground);
}

// Busca el campo en OpenStreetMap (Nominatim) dentro de Asturias y solo da por bueno un campo o recinto deportivo cuyo
// nombre contenga el del campo: «Hermanos Llana» → «Campo Municipal Hermanos Llana». Una calle o una escultura con el
// mismo nombre no cuentan. Una petición por segundo como mucho, como pide su política de uso.
internal sealed class NominatimGroundLocator(HttpClient http, IOptions<GroundOptions> options, TimeProvider clock) : IGroundLocator
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static DateTimeOffset _lastRequest = DateTimeOffset.MinValue;
    private static readonly HashSet<string> SportTypes = ["pitch", "stadium", "sports_centre", "sports_hall", "recreation_ground"];

    public async Task<GroundLocation?> LocateAsync(string name, CancellationToken cancellationToken)
    {
        foreach (var query in new[] { name, $"Campo de fútbol {name}" })
        {
            var results = await SearchAsync(query, cancellationToken);
            if (Pick(name, results) is { } found) return found;
        }
        return null;
    }

    internal static GroundLocation? Pick(string name, IReadOnlyList<JsonElement> results)
    {
        var wanted = Ground.KeyOf(name);
        foreach (var result in results)
        {
            var category = Text(result, "category");
            var type = Text(result, "type");
            if (category != "leisure" || type is null || !SportTypes.Contains(type)) continue;

            var label = Ground.KeyOf(Text(result, "name"));
            if (label.Length == 0 || !Contains(label, wanted)) continue;

            if (!decimal.TryParse(Text(result, "lat"), NumberStyles.Float, CultureInfo.InvariantCulture, out var lat)
                || !decimal.TryParse(Text(result, "lon"), NumberStyles.Float, CultureInfo.InvariantCulture, out var lon))
                continue;

            var address = result.TryGetProperty("address", out var a) && a.ValueKind == JsonValueKind.Object ? a : default;
            var locality = First(address, "city", "town", "village", "municipality", "county");
            var street = Join(" ", First(address, "road", "pedestrian", "footway"), First(address, "house_number"));
            var area = First(address, "suburb", "quarter", "neighbourhood", "hamlet");
            var place = Join(" ", First(address, "postcode"), locality);
            var full = Join(", ", street, area == locality ? null : area, place);
            return new GroundLocation(full, locality, lat, lon);
        }
        return null;
    }

    private async Task<IReadOnlyList<JsonElement>> SearchAsync(string query, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var url = "search?format=jsonv2&addressdetails=1&limit=8&countrycodes=es&bounded=1"
            + $"&viewbox={Uri.EscapeDataString(settings.ViewBox)}&q={Uri.EscapeDataString(query)}";

        await Gate.WaitAsync(cancellationToken);
        try
        {
            var wait = _lastRequest + TimeSpan.FromMilliseconds(settings.DelayMilliseconds) - clock.GetUtcNow();
            if (wait > TimeSpan.Zero) await Task.Delay(wait, clock, cancellationToken);
            try
            {
                using var response = await http.GetAsync(url, cancellationToken);
                response.EnsureSuccessStatusCode();
                var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
                return body.ValueKind == JsonValueKind.Array ? body.EnumerateArray().Select(e => e.Clone()).ToList() : [];
            }
            finally
            {
                _lastRequest = clock.GetUtcNow();
            }
        }
        finally
        {
            Gate.Release();
        }
    }

    // Todas las palabras del nombre buscado (sin artículos ni preposiciones sueltas) están en el del resultado.
    private static bool Contains(string label, string wanted)
    {
        if (label.Contains(wanted, StringComparison.Ordinal)) return true;
        var words = label.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        var needed = wanted.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => w.Length > 2).ToList();
        return needed.Count > 0 && needed.All(words.Contains);
    }

    private static string? Text(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string? First(JsonElement element, params string[] properties) =>
        properties.Select(p => Text(element, p)).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    private static string? Join(string separator, params string?[] parts)
    {
        var present = parts.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
        return present.Count == 0 ? null : string.Join(separator, present);
    }
}

// Catálogo de campos: los nuevos de los partidos y su ubicación, una vez al día, en la base de datos reales.
internal sealed class GroundsWorker(
    IServiceScopeFactory scopeFactory,
    JobScheduler scheduler,
    TimeProvider clock,
    ILogger<GroundsWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var realData = RealDataAmbient.Enter();
        var job = scheduler.Get(JobNames.Grounds);
        if (!job.Enabled) return;
        try
        {
            var due = await scheduler.FirstDueAsync(job.Name, stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                var manual = await scheduler.WaitAsync(job.Name, due, stoppingToken);
                try
                {
                    await scheduler.RunAsync(job.Name, manual, RunOnceAsync, stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
                {
                    logger.LogError(ex, "Error con el catálogo de campos");
                }

                due = job.Interval is { } interval ? clock.GetUtcNow() + interval : null;
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Apagado ordenado.
        }
    }

    private async Task<JobOutcome> RunOnceAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<SyncGroundsCommand, GroundsSynced>>();
        var result = await handler.Handle(new SyncGroundsCommand(), cancellationToken);
        var summary = $"{result.Added} {(result.Added == 1 ? "campo nuevo" : "campos nuevos")} · {result.Located} ubicados · {result.NotFound} sin encontrar"
            + (result.Failed > 0 ? $" · {result.Failed} sin respuesta del servicio de mapas" : string.Empty);
        return new JobOutcome(summary, result.Failed > 0 ? [$"{result.Failed} búsquedas sin respuesta de OpenStreetMap"] : []);
    }
}
