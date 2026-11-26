using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Common;
using TercerTiempo.Domain.Clubs;
using TercerTiempo.Domain.Players;
using TercerTiempo.Domain.Squads;

namespace TercerTiempo.Application.Search;

public sealed record SearchItemDto(
    [property: Description("competition, team, club, player, coach (cuerpo técnico), match, squad o referee.")]
    string Type,
    Guid Id,
    string Title,
    string? Subtitle,
    [property: Description("Ruta del frontal donde está la información (equipo, liga, partido, perfil del jugador...).")]
    string Link,
    [property: Description("Escudo (crest), foto del jugador (photo) o escudo del local (match).")]
    string? ImageUrl,
    string ImageKind,
    [property: Description("Escudo del equipo del jugador o del visitante del partido.")]
    string? SecondaryImageUrl,
    [property: Description("Resultado del partido («0-1» o «vs») o número de jornadas de la liga.")]
    string? Meta,
    DateTimeOffset? Date,
    IReadOnlyList<string> Badges,
    double Score);

public sealed record SearchGroupDto(string Type, string Label, int Total, IReadOnlyList<SearchItemDto> Items);

public sealed record SearchResultsDto(
    string Query,
    [property: Description("Palabras buscadas ya normalizadas (sin tildes), para resaltarlas.")]
    IReadOnlyList<string> Terms,
    [property: Description("true si no había nada con todas las palabras y se enseña lo que coincide con casi todas.")]
    bool Partial,
    int Total,
    IReadOnlyList<SearchGroupDto> Groups,
    DateTimeOffset IndexedAt,
    double ElapsedMs);

public sealed record SearchQuery(
    [property: Description("Lo que se busca: nombres de ligas, equipos, clubes, jugadores, técnicos o dos equipos para un partido («nalón roces»).")]
    string? Q,
    [property: Description("Solo un tipo: competition, team, club, player, coach, match, squad o referee.")]
    string? Type,
    [property: Description("Resultados por tipo (1-50; por defecto 6).")]
    int? Limit) : IQuery<SearchResultsDto>;

// El índice se comparte entre peticiones y una búsqueda nunca espera a rehacerlo, salvo la primera (aún no hay ninguno):
// si han cambiado los datos que enlaza (plantillas, fichas, clubes...) o ha pasado su vigencia, se rehace en segundo
// plano y mientras tanto se sigue buscando en el anterior (medio segundo; un enlace a algo recién borrado da «no
// encontrado»). Si alguien ha buscado hace poco, se empieza a rehacer en cuanto cambian los datos, para que la
// siguiente búsqueda ya lo vea; si no, al buscar. Como mucho uno cada pocos segundos: una sincronización guarda muchas
// veces seguidas.
public sealed class SearchIndexHolder(IServiceScopeFactory scopes, TimeProvider clock, ILogger<SearchIndexHolder> logger)
{
    // Cada guardado que toca lo que se busca ya lo marca para rehacer (Invalidate); esto es solo por si algo cambia por fuera.
    private static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(30);
    internal static readonly TimeSpan MinRebuildGap = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan RecentUse = TimeSpan.FromMinutes(10);
    private readonly SemaphoreSlim _building = new(1, 1);
    private volatile SearchIndex? _index;
    private volatile bool _stale;
    private int _scheduled;
    private long _lastUsedTicks;
    private long _lastAttemptTicks;

    internal async Task<SearchIndex> GetAsync(CancellationToken cancellationToken)
    {
        Interlocked.Exchange(ref _lastUsedTicks, clock.GetUtcNow().UtcTicks);
        if (_index is not { } current)
        {
            await RebuildAsync(cancellationToken);
            return _index!;
        }

        if (_stale || clock.GetUtcNow() - current.BuiltAt > MaxAge) Schedule();
        return current;
    }

    // Los datos han cambiado (sincronización de las ligas, plantillas, fichas).
    public void Invalidate()
    {
        _stale = true;
        if (_index is not null && clock.GetUtcNow().UtcTicks - Interlocked.Read(ref _lastUsedTicks) < RecentUse.Ticks) Schedule();
    }

    private void Schedule()
    {
        if (Interlocked.Exchange(ref _scheduled, 1) == 1) return;
        _ = Task.Run(async () =>
        {
            try
            {
                var wait = MinRebuildGap.Ticks - (clock.GetUtcNow().UtcTicks - Interlocked.Read(ref _lastAttemptTicks));
                if (wait > 0) await Task.Delay(TimeSpan.FromTicks(wait), clock);
            }
            finally
            {
                // Antes de rehacerlo: lo que cambie mientras se rehace vuelve a programar otro.
                Volatile.Write(ref _scheduled, 0);
            }
            await RebuildAsync(CancellationToken.None);
        });
    }

    private async Task RebuildAsync(CancellationToken cancellationToken)
    {
        await _building.WaitAsync(cancellationToken);
        try
        {
            if (_index is not null && !_stale && clock.GetUtcNow() - _index.BuiltAt <= MaxAge) return;
            _stale = false;
            Interlocked.Exchange(ref _lastAttemptTicks, clock.GetUtcNow().UtcTicks);
            using var scope = scopes.CreateScope();
            var corpus = await scope.ServiceProvider.GetRequiredService<ISearchCorpusSource>().LoadAsync(cancellationToken);
            var index = SearchIndex.Build(corpus, clock.GetUtcNow());
            _index = index;
            logger.LogInformation("Índice del buscador rehecho: {Count} documentos", index.Count);
        }
        catch (Exception ex) when (_index is not null)
        {
            // Se vuelve a intentar en la siguiente búsqueda.
            _stale = true;
            logger.LogWarning(ex, "No se pudo rehacer el índice del buscador; se sigue con el anterior");
        }
        finally
        {
            _building.Release();
        }
    }
}

internal sealed class SearchHandler(
    ICurrentUser currentUser,
    SearchIndexHolder holder,
    IClubStore clubs,
    ISquadStore squads,
    IPlayerStore players,
    TimeProvider clock) : IQueryHandler<SearchQuery, SearchResultsDto>
{
    public const int DefaultLimit = 6;
    public const int MaxLimit = 50;

    public async Task<SearchResultsDto> Handle(SearchQuery query, CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is not { } userId) throw new UnauthenticatedException();

        var text = (query.Q ?? string.Empty).Trim();
        if (text.Length > 100) text = text[..100];
        var type = query.Type is { Length: > 0 } t && SearchTypes.IsKnown(t.Trim().ToLowerInvariant()) ? t.Trim().ToLowerInvariant() : null;
        var limit = Math.Clamp(query.Limit ?? DefaultLimit, 1, MaxLimit);

        var index = await holder.GetAsync(cancellationToken);
        var outcome = index.Search(text, await ViewerAsync(userId, cancellationToken), type);

        var groups = outcome.Hits
            .GroupBy(h => h.Doc.Type)
            .Select(g => new SearchGroupDto(g.Key, SearchTypes.Labels[g.Key], g.Count(), g.Take(limit).Select(ToDto).ToList()))
            .OrderByDescending(g => g.Items[0].Score)
            .ToList();

        return new SearchResultsDto(text, outcome.Terms, outcome.Partial, outcome.Hits.Count, groups, index.BuiltAt, Math.Round(outcome.ElapsedMs, 1));
    }

    // Equipos propios (club: los suyos; técnico: los de sus plantillas; jugador: el suyo) y plantillas internas visibles.
    private async Task<SearchViewer> ViewerAsync(Guid userId, CancellationToken cancellationToken)
    {
        // El administrador encuentra también las plantillas internas de todos los equipos (para verlas, no editarlas).
        if (currentUser.IsAdmin) return new SearchViewer(new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>(), SeesAll: true);

        var today = SquadAccess.Today(clock);
        var teams = new HashSet<Guid>();
        var visible = new HashSet<Guid>();
        var clubTeams = new HashSet<Guid>();

        if (currentUser.ClubId is { } clubId && await clubs.GetByIdAsync(clubId, cancellationToken) is { } club)
            foreach (var team in club.Teams)
            {
                teams.Add(team.TeamId);
                clubTeams.Add(team.TeamId);
            }

        if (currentUser.CoachId is { } coachId)
            foreach (var squad in await squads.GetRefsByCoachAsync(coachId, today, cancellationToken))
            {
                teams.Add(squad.TeamId);
                visible.Add(squad.Id);
            }

        if (await players.GetByUserIdAsync(userId, cancellationToken) is { } player
            && await squads.GetByPlayerAsync(player.Id, cancellationToken) is { } own)
        {
            teams.Add(own.TeamId);
            visible.Add(own.Id);
        }

        return new SearchViewer(teams, visible, clubTeams);
    }

    private static SearchItemDto ToDto(SearchHit hit) => new(
        hit.Doc.Type, hit.Doc.Id, hit.Doc.Title, hit.Doc.Subtitle, hit.Link, hit.Doc.ImageUrl, hit.Doc.ImageKind,
        hit.Doc.SecondaryImageUrl, hit.Doc.Meta, hit.Doc.Date, hit.Badges, hit.Score);
}
