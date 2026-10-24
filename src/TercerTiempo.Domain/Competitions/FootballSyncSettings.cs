using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Common.Errors;

namespace TercerTiempo.Domain.Competitions;

public enum SyncScopeKind
{
    CurrentRound = 1,
    DateRange = 2,
    FullSeason = 3,
}

// Qué descarga la sincronización automática de las ligas. Por defecto solo la jornada en curso (los partidos de unos días
// antes y después de hoy, con sus resultados y las clasificaciones) y, cada cierto número de días, la temporada entera
// con plantillas para descubrir partidos nuevos y fichajes. Una sola fila, editable por el administrador.
public sealed class FootballSyncSettings : AggregateRoot
{
    public static readonly Guid SingletonId = new("01a16f00-0000-7000-8000-000000000002");

    public const int MaxRoundDays = 14;
    public const int MaxFullSyncEveryDays = 60;

    public SyncScopeKind ScheduledScope { get; private set; }

    // La jornada en curso va de hoy menos DaysBack a hoy más DaysAhead (días enteros).
    public int DaysBack { get; private set; }
    public int DaysAhead { get; private set; }

    public bool RostersInRound { get; private set; }

    // Cada cuántos días la pasada automática es completa en lugar de solo la jornada; 0 = nunca (solo a mano).
    public int FullSyncEveryDays { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    private FootballSyncSettings() { }

    public static FootballSyncSettings CreateDefault(DateTimeOffset now) => new()
    {
        Id = SingletonId,
        ScheduledScope = SyncScopeKind.CurrentRound,
        DaysBack = 3,
        DaysAhead = 4,
        RostersInRound = false,
        FullSyncEveryDays = 7,
        UpdatedAt = now,
    };

    public (DateOnly From, DateOnly To) RoundWindow(DateOnly today) => (today.AddDays(-DaysBack), today.AddDays(DaysAhead));

    public bool FullSyncDue(DateTimeOffset? lastFullSyncAt, DateTimeOffset now) =>
        ScheduledScope == SyncScopeKind.FullSeason
        || (FullSyncEveryDays > 0 && (lastFullSyncAt is null || now - lastFullSyncAt.Value >= TimeSpan.FromDays(FullSyncEveryDays)));

    public void Update(SyncScopeKind scheduledScope, int daysBack, int daysAhead, bool rostersInRound, int fullSyncEveryDays, DateTimeOffset now)
    {
        // Unas fechas fijas se quedan viejas: la pasada automática solo puede ser la jornada en curso o la temporada.
        if (scheduledScope is not (SyncScopeKind.CurrentRound or SyncScopeKind.FullSeason))
            throw new DomainException(ErrorCode.SyncScheduledScopeInvalid);

        ScheduledScope = scheduledScope;
        DaysBack = Guard.InRange(daysBack, 0, MaxRoundDays, "días antes de hoy");
        DaysAhead = Guard.InRange(daysAhead, 0, MaxRoundDays, "días después de hoy");
        RostersInRound = rostersInRound;
        FullSyncEveryDays = Guard.InRange(fullSyncEveryDays, 0, MaxFullSyncEveryDays, "días entre pasadas completas");
        UpdatedAt = now;
    }
}

public interface IFootballSyncSettingsStore
{
    // Para leer (sin seguimiento de cambios); null si la fila aún no existe.
    Task<FootballSyncSettings?> GetAsync(CancellationToken cancellationToken = default);
    Task<FootballSyncSettings?> GetForUpdateAsync(CancellationToken cancellationToken = default);
    void Add(FootballSyncSettings settings);
}
