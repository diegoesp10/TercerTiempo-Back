using System.ComponentModel;
using System.Globalization;
using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Jobs;
using TercerTiempo.Application.Trainings;
using TercerTiempo.Domain.Clubs;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Grounds;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Teams;

namespace TercerTiempo.Application.Grounds;

// Busca dónde está un campo de fútbol (dirección y punto exacto). null si no lo encuentra; lanza si el servicio falla, y
// entonces el intento no cuenta.
public interface IGroundLocator
{
    Task<GroundLocation?> LocateAsync(string name, CancellationToken cancellationToken);
}

public sealed record GroundDto(
    Guid Id,
    string Name,
    string? Address,
    string? Locality,
    decimal? Latitude,
    decimal? Longitude,
    [property: Description("Hay punto exacto en el mapa (de OpenStreetMap). Sin él, mapsUrl abre la búsqueda del campo por su nombre.")]
    bool Located,
    [property: Description("Enlace a Google Maps: al punto exacto o, si no está ubicado, a la búsqueda del campo.")]
    string MapsUrl,
    [property: Description("Equipos de la liga que juegan ahí como locales (su campo habitual).")]
    IReadOnlyList<string> HomeTeams)
{
    public static GroundDto From(Ground ground, IReadOnlyList<string>? homeTeams = null) =>
        new(ground.Id, ground.Name, ground.Address, ground.Locality, ground.Latitude, ground.Longitude, ground.IsLocated, ground.MapsUrl, homeTeams ?? []);
}

public sealed record SquadGroundsDto(
    [property: Description("El campo habitual del equipo de la plantilla como local; null si sus partidos no traen campo.")]
    Guid? HomeGroundId,
    [property: Description("Los campos de la liga (los de los partidos de la temporada): primero el de casa y luego por nombre.")]
    IReadOnlyList<GroundDto> Grounds);

public sealed record GetSquadGroundsQuery(Guid SquadId) : IQuery<SquadGroundsDto>;

public sealed record SyncGroundsCommand : ICommand<GroundsSynced>
{
    public const int LocatePerRun = 60;
}

public sealed record GroundsSynced(int Added, int Located, int NotFound, int Failed);

// Los campos de una temporada a partir de sus partidos: el habitual de cada equipo es en el que más partidos ha jugado
// como local, y el de un partido, el que trae el partido o, si no trae ninguno, el habitual del equipo de casa.
internal sealed class SeasonGrounds
{
    private static readonly CultureInfo Spanish = CultureInfo.GetCultureInfo("es-ES");
    private readonly IReadOnlyList<SeasonGround> _grounds;
    private readonly Dictionary<Guid, Ground> _homeByTeam;

    private SeasonGrounds(IReadOnlyList<SeasonGround> grounds)
    {
        _grounds = grounds;
        _homeByTeam = grounds
            .SelectMany(g => g.HomeMatchesByTeam.Select(t => (TeamId: t.Key, Matches: t.Value, g.Ground)))
            .GroupBy(x => x.TeamId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.Matches).ThenBy(x => x.Ground.Name, StringComparer.Ordinal).First().Ground);
    }

    public static async Task<SeasonGrounds> LoadAsync(IGroundStore grounds, Guid seasonId, CancellationToken cancellationToken) =>
        new(await grounds.GetBySeasonAsync(seasonId, cancellationToken));

    public Ground? HomeOf(Guid teamId) => _homeByTeam.GetValueOrDefault(teamId);

    public Ground? OfMatch(Match match) =>
        (match.Venue is { } venue ? _grounds.FirstOrDefault(g => SameName(g.Ground.Name, venue))?.Ground : null) ?? HomeOf(match.HomeTeamId);

    public Ground? Find(Guid groundId) => _grounds.FirstOrDefault(g => g.Ground.Id == groundId)?.Ground;

    public IReadOnlyList<Ground> All => _grounds.Select(g => g.Ground).ToList();

    public IReadOnlyList<Guid> HomeTeamsOf(Guid groundId) =>
        _homeByTeam.Where(x => x.Value.Id == groundId).Select(x => x.Key).ToList();

    public GroundDto ToDto(Ground ground, IReadOnlyDictionary<Guid, string> teamNames) =>
        GroundDto.From(ground, HomeTeamsOf(ground.Id).Select(id => teamNames.GetValueOrDefault(id)).OfType<string>().Order(StringComparer.Create(Spanish, true)).ToList());

    private static bool SameName(string a, string b) => string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
}

// Los campos de la liga de la plantilla, para elegir dónde se cita a los convocados.
internal sealed class GetSquadGroundsHandler(ICurrentUser currentUser, ISquadStore squads, IClubStore clubs, IGroundStore grounds, ITeamStore teams, TimeProvider clock)
    : IQueryHandler<GetSquadGroundsQuery, SquadGroundsDto>
{
    private static readonly StringComparer SpanishOrder = StringComparer.Create(CultureInfo.GetCultureInfo("es-ES"), true);

    public async Task<SquadGroundsDto> Handle(GetSquadGroundsQuery query, CancellationToken cancellationToken)
    {
        var squad = await TrainingAccess.SquadForViewerAsync(squads, clubs, currentUser, query.SquadId, clock, cancellationToken);
        var season = await SeasonGrounds.LoadAsync(grounds, squad.SeasonId, cancellationToken);
        var teamIds = season.All.SelectMany(g => season.HomeTeamsOf(g.Id)).Distinct().ToList();
        var names = (await teams.GetByIdsAsync(teamIds, cancellationToken)).ToDictionary(t => t.Id, t => t.Name);
        var home = season.HomeOf(squad.TeamId);

        var list = season.All
            .OrderBy(g => g.Id != home?.Id)
            .ThenBy(g => g.Name, SpanishOrder)
            .Select(g => season.ToDto(g, names))
            .ToList();
        return new SquadGroundsDto(home?.Id, list);
    }
}

// Catálogo de campos (base de datos reales): da de alta los campos nuevos que traen los partidos y busca dónde están
// los que aún no tienen ubicación, unos pocos por pasada y respetando el límite del servicio de mapas.
internal sealed class SyncGroundsHandler(IGroundStore grounds, IGroundLocator locator, IJobProgress progress, IUnitOfWork unitOfWork, TimeProvider clock)
    : ICommandHandler<SyncGroundsCommand, GroundsSynced>
{
    private const string Job = JobNames.Grounds;
    private const int MaxFailuresInARow = 3;

    public async Task<GroundsSynced> Handle(SyncGroundsCommand command, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var added = 0;
        foreach (var name in (await grounds.GetUncataloguedVenuesAsync(cancellationToken))
                     .Select(n => n.Trim())
                     .Where(n => n.Length is > 0 and <= Ground.MaxNameLength)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            grounds.Add(Ground.Create(name, now));
            added++;
        }
        if (added > 0)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
            progress.Event(Job, $"{added} {(added == 1 ? "campo nuevo" : "campos nuevos")} en el catálogo", JobEventTone.Success);
        }

        var pending = await grounds.GetToLocateAsync(now, SyncGroundsCommand.LocatePerRun, cancellationToken);
        var (located, notFound, failed, inARow, done) = (0, 0, 0, 0, 0);
        foreach (var ground in pending)
        {
            progress.Step(Job, ground.Name, done++, pending.Count);
            GroundLocation? found;
            try
            {
                found = await locator.LocateAsync(ground.Name, cancellationToken);
                inARow = 0;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
            {
                failed++;
                progress.Event(Job, $"{ground.Name}: el servicio de mapas no responde ({ex.Message}); se reintenta en la próxima pasada", JobEventTone.Warning);
                if (++inARow >= MaxFailuresInARow) break;
                continue;
            }

            ground.Locate(found, clock.GetUtcNow());
            if (found is null)
            {
                notFound++;
                progress.Event(Job, $"{ground.Name}: no aparece como campo de fútbol en OpenStreetMap (intento {ground.LocateAttempts} de {Ground.MaxLocateAttempts})");
            }
            else
            {
                located++;
                progress.Event(Job, $"{ground.Name}: {found.Address ?? found.Locality ?? "ubicado"}", JobEventTone.Success);
            }
        }

        if (located + notFound > 0) await unitOfWork.SaveChangesAsync(cancellationToken);
        progress.Step(Job, null, pending.Count, pending.Count);
        return new GroundsSynced(added, located, notFound, failed);
    }
}
