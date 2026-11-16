namespace TercerTiempo.Domain.Common;

public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    // Tras guardar, deja de seguir todo salvo lo indicado (que sigue seguido sin cambios): para trabajos largos que guardan
    // por tandas y no deben ir acumulando entidades. Por defecto no hace nada.
    void ResetTracking(IEnumerable<object> keep) { }
}
