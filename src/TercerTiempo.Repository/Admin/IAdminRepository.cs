using TercerTiempo.Application.Sync;

namespace TercerTiempo.Repository.Admin;

/// <summary>Operaciones de administración (sincronización con el proveedor de datos).</summary>
public interface IAdminRepository
{
    Task<SyncResultDto> SyncSeasonAsync(CancellationToken cancellationToken = default);
}
