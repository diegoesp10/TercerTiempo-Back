using TercerTiempo.Application.Seasons;
using TercerTiempo.Domain.Competitions;

namespace TercerTiempo.Application.Tests;

// Sin temporadas de la app: cada liga usa su temporada en curso, como en una base recién creada.
internal sealed class NoSeasons : ISeasonContext
{
    public Task<SeasonYear?> SelectedAsync(CancellationToken cancellationToken = default) => Task.FromResult<SeasonYear?>(null);
    public Task<SeasonYear?> CurrentAsync(CancellationToken cancellationToken = default) => Task.FromResult<SeasonYear?>(null);
    public Task EnsureEditableAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}
