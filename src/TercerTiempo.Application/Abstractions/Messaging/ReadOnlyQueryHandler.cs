namespace TercerTiempo.Application.Abstractions.Messaging;

// Lecturas sin seguir cambios: mientras dura, el contexto de datos del ámbito no guarda una copia de cada fila que lee.
// Lo que se añade (Add) sí se guarda al hacer SaveChanges, así que una consulta que crea algo al vuelo (la licencia de
// prueba de un club, LicenseGate.EnsureForClubAsync) sigue funcionando; modificar algo leído dentro no se guardaría.
public interface IReadScope
{
    IDisposable Begin();
}

internal sealed class NoReadScope : IReadScope
{
    public IDisposable Begin() => Nothing.Instance;

    private sealed class Nothing : IDisposable
    {
        public static readonly Nothing Instance = new();
        public void Dispose() { }
    }
}

internal sealed class ReadOnlyQueryHandler<TQuery, TResult>(IQueryHandler<TQuery, TResult> inner, IReadScope scope) : IQueryHandler<TQuery, TResult>
    where TQuery : IQuery<TResult>
{
    public async Task<TResult> Handle(TQuery query, CancellationToken cancellationToken)
    {
        using (scope.Begin())
            return await inner.Handle(query, cancellationToken);
    }
}
