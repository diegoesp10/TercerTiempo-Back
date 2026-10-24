using System.Globalization;
using TercerTiempo.Application.Abstractions.FootballData;
using TercerTiempo.Domain.Competitions;

namespace TercerTiempo.Application.Sync;

// Qué abarca una sincronización: la jornada en curso o unas fechas (solo los partidos de esos días, con sus resultados y
// las clasificaciones) o la temporada entera; opcionalmente solo algunas ligas y con o sin plantillas.
public sealed record SyncScope(
    SyncScopeKind Kind,
    DateOnly? From,
    DateOnly? To,
    IReadOnlyList<Guid> CompetitionIds,
    bool IncludeRosters)
{
    private static readonly CultureInfo Spanish = CultureInfo.GetCultureInfo("es-ES");

    public static SyncScope FullSeason(IReadOnlyList<Guid>? competitionIds = null, bool includeRosters = true) =>
        new(SyncScopeKind.FullSeason, null, null, competitionIds ?? [], includeRosters);

    public static SyncScope Round(FootballSyncSettings settings, DateOnly today, IReadOnlyList<Guid>? competitionIds = null, bool? includeRosters = null)
    {
        var (from, to) = settings.RoundWindow(today);
        return new(SyncScopeKind.CurrentRound, from, to, competitionIds ?? [], includeRosters ?? settings.RostersInRound);
    }

    public MatchWindow? Window => Kind != SyncScopeKind.FullSeason && From is { } from && To is { } to ? new MatchWindow(from, to) : null;

    public string Describe()
    {
        var text = Kind switch
        {
            SyncScopeKind.CurrentRound => $"Jornada en curso ({Day(From)} – {Day(To)})",
            SyncScopeKind.DateRange => $"Del {Day(From)} al {Day(To)}",
            _ => "Temporada completa",
        };

        if (CompetitionIds.Count > 0) text += CompetitionIds.Count == 1 ? " · 1 liga" : $" · {CompetitionIds.Count} ligas";
        if (Kind == SyncScopeKind.FullSeason && !IncludeRosters) text += " · sin plantillas";
        if (Kind != SyncScopeKind.FullSeason && IncludeRosters) text += " · con plantillas";
        return text;
    }

    private static string Day(DateOnly? day) => day?.ToString("d MMM", Spanish).TrimEnd('.') ?? "?";
}

public static class FootballSyncPlanner
{
    // La pasada automática: la temporada entera si toca (nunca se ha hecho o han pasado los días configurados) y, si no,
    // solo la jornada en curso.
    public static SyncScope Scheduled(FootballSyncSettings settings, DateTimeOffset? lastFullSyncAt, DateTimeOffset now) =>
        settings.FullSyncDue(lastFullSyncAt, now)
            ? SyncScope.FullSeason()
            : SyncScope.Round(settings, DateOnly.FromDateTime(now.UtcDateTime));

    public static DateTimeOffset? NextFullSyncAt(FootballSyncSettings settings, DateTimeOffset? lastFullSyncAt) =>
        settings.ScheduledScope == SyncScopeKind.FullSeason || settings.FullSyncEveryDays == 0 || lastFullSyncAt is null
            ? null
            : lastFullSyncAt.Value.AddDays(settings.FullSyncEveryDays);
}
