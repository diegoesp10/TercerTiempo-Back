namespace TercerTiempo.Domain.OfficialSheets;

// Lo que hace falta de un acta guardada para decidir si toca buscarla otra vez, sin cargar sus líneas ni sus sucesos.
public sealed record OfficialSheetState(Guid MatchId, OfficialSheetStatus Status, DateTimeOffset CheckedAt, string Provider)
{
    public static OfficialSheetState From(OfficialMatchSheet sheet) => new(sheet.MatchId, sheet.Status, sheet.CheckedAt, sheet.Provider);
}

public interface IOfficialSheetStore
{
    Task<OfficialMatchSheet?> GetByMatchAsync(Guid matchId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OfficialMatchSheet>> GetByMatchIdsAsync(IEnumerable<Guid> matchIds, CancellationToken cancellationToken = default);

    // Las dos anteriores no devuelven las actas sacadas de webs que ya no están en OfficialSheets:Sources (se ocultan,
    // no se borran). Estas sí, para que la descarga pueda sustituirlas por la de una web activa.
    Task<OfficialMatchSheet?> GetStoredByMatchAsync(Guid matchId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OfficialMatchSheet>> GetStoredByMatchIdsAsync(IEnumerable<Guid> matchIds, CancellationToken cancellationToken = default);
    // Las actas disponibles (de webs activas) de esos partidos con sus líneas pero sin sucesos, solo para leer.
    Task<IReadOnlyList<OfficialMatchSheet>> GetAvailableLinesByMatchIdsAsync(IEnumerable<Guid> matchIds, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OfficialSheetState>> GetStoredStatesAsync(IEnumerable<Guid> matchIds, CancellationToken cancellationToken = default);

    // Actas de esa web guardadas antes de leer su cuerpo técnico (para releerlo de la página ya descargada), con solo el
    // cuerpo técnico cargado y para modificar.
    Task<IReadOnlyList<OfficialMatchSheet>> GetWithoutStaffAsync(string provider, int take, CancellationToken cancellationToken = default);

    // El cuerpo técnico de las actas (disponibles o provisionales, de webs activas) de esos partidos, solo para leer.
    Task<IReadOnlyList<OfficialMatchSheet>> GetStaffByMatchIdsAsync(IEnumerable<Guid> matchIds, CancellationToken cancellationToken = default);
    void Add(OfficialMatchSheet sheet);
}
