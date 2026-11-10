using System.Collections.Concurrent;
using System.ComponentModel;
using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Common;
using TercerTiempo.Domain.Clubs;
using TercerTiempo.Domain.Coaches;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Players;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Teams;

namespace TercerTiempo.Application.Presence;

public static class PresenceStatus
{
    public const string Online = "online";
    public const string Idle = "idle";
}

public sealed record PresenceSessionDto(
    [property: Description("Un inicio de sesión (el jti de su token): la misma cuenta en dos dispositivos son dos sesiones.")]
    string SessionId,
    Guid UserId,
    AccountType AccountType,
    string Name,
    string? Email,
    [property: Description("Equipo o equipos de la cuenta: el del jugador, los de las plantillas del técnico o los del club.")]
    string? Teams,
    [property: Description("Página de la web en la que está, tal como la manda el navegador (/plantillas/…).")]
    string? Path,
    [property: Description("Navegador y sistema sacados del User-Agent: «Chrome · Windows», «Safari · iPhone».")]
    string Device,
    [property: Description("online: ha dado señales en los últimos 150 segundos; idle: en los últimos 30 minutos.")]
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset LastSeenAt);

public sealed record PresenceOverviewDto(
    int Online,
    int Idle,
    int Clubs,
    int Coaches,
    int Players,
    [property: Description("Sesiones que cumplen la búsqueda (se devuelven como mucho 500, las más recientes).")]
    int Matching,
    IReadOnlyList<PresenceSessionDto> Sessions,
    DateTimeOffset GeneratedAt);

public sealed record RecordPresenceCommand(string? Path, string? UserAgent = null) : ICommand<bool>;

public sealed record EndPresenceCommand : ICommand<bool>;

public sealed record GetPresenceQuery(string? Q = null) : IQuery<PresenceOverviewDto>;

internal sealed record PresenceProfile(string Name, string? Teams);

// Quién tiene la web abierta con su cuenta: cada sesión con la página en la que está y su última señal. Vive en memoria
// porque es solo para verlo en la administración; tras reiniciar la API se rellena sola con el siguiente latido de cada
// navegador (uno por minuto mientras la pestaña está a la vista).
internal sealed class PresenceBoard(TimeProvider clock)
{
    public static readonly TimeSpan OnlineFor = TimeSpan.FromSeconds(150);
    public static readonly TimeSpan KeepFor = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan ProfileFor = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan PruneEvery = TimeSpan.FromMinutes(1);
    private const int MaxListed = 500;

    private readonly ConcurrentDictionary<string, Entry> _sessions = new();
    private DateTimeOffset _prunedAt = DateTimeOffset.MinValue;

    // El nombre y los equipos se leen de la base al empezar la sesión y cada 10 minutos, no en cada latido.
    public bool NeedsProfile(string sessionId) =>
        !_sessions.TryGetValue(sessionId, out var entry) || clock.GetUtcNow() - entry.ProfileAt > ProfileFor;

    public void Touch(string sessionId, Guid userId, AccountType type, string? email, string? path, string device, PresenceProfile? profile)
    {
        var now = clock.GetUtcNow();
        _sessions.AddOrUpdate(sessionId,
            _ => new Entry(sessionId, userId, type, email, profile ?? new PresenceProfile(email ?? "Cuenta", null),
                profile is null ? DateTimeOffset.MinValue : now, path, device, now, now),
            (_, old) => old with
            {
                Email = email ?? old.Email,
                Profile = profile ?? old.Profile,
                ProfileAt = profile is null ? old.ProfileAt : now,
                Path = path ?? old.Path,
                Device = device,
                LastSeenAt = now,
            });
        if (now - _prunedAt > PruneEvery) Prune(now);
    }

    public void End(string sessionId) => _sessions.TryRemove(sessionId, out _);

    public PresenceOverviewDto Overview(string? query)
    {
        var now = clock.GetUtcNow();
        Prune(now);
        var all = _sessions.Values.ToList();
        var terms = NameNormalizer.Normalize(query).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var matching = all
            .Where(e => terms.Length == 0 || terms.All(NameNormalizer.Normalize($"{e.Profile.Name} {e.Email} {e.Profile.Teams}").Contains))
            .OrderByDescending(e => e.LastSeenAt)
            .ToList();

        return new PresenceOverviewDto(
            all.Count(e => IsOnline(e, now)),
            all.Count(e => !IsOnline(e, now)),
            all.Count(e => e.Type == AccountType.Club),
            all.Count(e => e.Type == AccountType.Coach),
            all.Count(e => e.Type == AccountType.Player),
            matching.Count,
            matching.Take(MaxListed).Select(e => new PresenceSessionDto(
                e.SessionId, e.UserId, e.Type, e.Profile.Name, e.Email, e.Profile.Teams, e.Path, e.Device,
                IsOnline(e, now) ? PresenceStatus.Online : PresenceStatus.Idle, e.StartedAt, e.LastSeenAt)).ToList(),
            now);
    }

    private static bool IsOnline(Entry entry, DateTimeOffset now) => now - entry.LastSeenAt <= OnlineFor;

    private void Prune(DateTimeOffset now)
    {
        _prunedAt = now;
        foreach (var (key, entry) in _sessions)
            if (now - entry.LastSeenAt > KeepFor) _sessions.TryRemove(key, out _);
    }

    private sealed record Entry(
        string SessionId, Guid UserId, AccountType Type, string? Email, PresenceProfile Profile, DateTimeOffset ProfileAt,
        string? Path, string Device, DateTimeOffset StartedAt, DateTimeOffset LastSeenAt);
}

// «Chrome · Windows», «Safari · iPhone»: lo justo para reconocer el dispositivo, sin guardar el User-Agent entero.
internal static class DeviceLabel
{
    public static string From(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent)) return "Desconocido";
        bool Has(string part) => userAgent.Contains(part, StringComparison.Ordinal);

        // El orden importa: Edge, Opera y Samsung Internet también dicen Chrome, y Chrome también dice Safari.
        var browser = Has("Edg/") || Has("EdgA/") || Has("EdgiOS/") ? "Edge"
            : Has("OPR/") || Has("Opera") ? "Opera"
            : Has("SamsungBrowser/") ? "Samsung Internet"
            : Has("Firefox/") || Has("FxiOS/") ? "Firefox"
            : Has("Chrome/") || Has("CriOS/") ? "Chrome"
            : Has("Safari/") ? "Safari"
            : "Navegador";
        var system = Has("iPhone") ? "iPhone"
            : Has("iPad") ? "iPad"
            : Has("Android") ? "Android"
            : Has("Windows") ? "Windows"
            : Has("Mac OS X") || Has("Macintosh") ? "Mac"
            : Has("Linux") ? "Linux"
            : null;
        return system is null ? browser : $"{browser} · {system}";
    }
}

internal sealed class RecordPresenceHandler(
    ICurrentUser currentUser,
    PresenceBoard board,
    IPlayerStore players,
    ICoachStore coaches,
    IClubStore clubs,
    ISquadStore squads,
    ITeamStore teams,
    TimeProvider clock) : ICommandHandler<RecordPresenceCommand, bool>
{
    private const int MaxPath = 200;

    public async Task<bool> Handle(RecordPresenceCommand command, CancellationToken cancellationToken)
    {
        // Solo las cuentas (club, cuerpo técnico y jugadores): el administrador no cuenta como gente en sesión.
        if (!currentUser.IsAuthenticated || currentUser.IsAdmin || currentUser.UserId is not { } userId || currentUser.AccountType is not { } type)
            return false;

        var session = currentUser.SessionId ?? userId.ToString("N");
        var profile = board.NeedsProfile(session) ? await ProfileAsync(userId, type, cancellationToken) : null;
        board.Touch(session, userId, type, currentUser.Email, CleanPath(command.Path), DeviceLabel.From(command.UserAgent), profile);
        return true;
    }

    private static string? CleanPath(string? path) =>
        string.IsNullOrWhiteSpace(path) || !path.StartsWith('/') ? null : path.Length > MaxPath ? path[..MaxPath] : path;

    private async Task<PresenceProfile> ProfileAsync(Guid userId, AccountType type, CancellationToken cancellationToken)
    {
        if (type == AccountType.Player && await players.GetByUserIdAsync(userId, cancellationToken) is { } player)
        {
            var own = await squads.GetByPlayerAsync(player.Id, cancellationToken);
            return new PresenceProfile(player.DisplayName, own is null ? null : await TeamNamesAsync([own.TeamId], cancellationToken));
        }

        if (type == AccountType.Coach && await coaches.GetByUserIdAsync(userId, cancellationToken) is { } coach)
        {
            var coached = await squads.GetByCoachAsync(coach.Id, SquadAccess.Today(clock), cancellationToken);
            return new PresenceProfile(coach.DisplayName, await TeamNamesAsync(coached.Select(s => s.TeamId), cancellationToken));
        }

        if (type == AccountType.Club && await clubs.GetByUserIdAsync(userId, cancellationToken) is { } club)
            return new PresenceProfile(club.Name, await TeamNamesAsync(club.Teams.Select(t => t.TeamId), cancellationToken));

        return new PresenceProfile(currentUser.Email ?? "Cuenta", null);
    }

    private async Task<string?> TeamNamesAsync(IEnumerable<Guid> teamIds, CancellationToken cancellationToken)
    {
        var names = (await teams.GetByIdsAsync(teamIds.Distinct(), cancellationToken)).Select(t => t.Name).OrderBy(n => n).ToList();
        return names.Count == 0 ? null : string.Join(", ", names);
    }
}

// Al salir de la cuenta la sesión desaparece al momento, sin esperar a que caduque.
internal sealed class EndPresenceHandler(ICurrentUser currentUser, PresenceBoard board) : ICommandHandler<EndPresenceCommand, bool>
{
    public Task<bool> Handle(EndPresenceCommand command, CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is not { } userId) return Task.FromResult(false);
        board.End(currentUser.SessionId ?? userId.ToString("N"));
        return Task.FromResult(true);
    }
}

internal sealed class GetPresenceHandler(PresenceBoard board) : IQueryHandler<GetPresenceQuery, PresenceOverviewDto>
{
    public Task<PresenceOverviewDto> Handle(GetPresenceQuery query, CancellationToken cancellationToken) =>
        Task.FromResult(board.Overview(query.Q));
}
