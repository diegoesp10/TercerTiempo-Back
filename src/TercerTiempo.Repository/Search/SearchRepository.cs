using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Search;

namespace TercerTiempo.Repository.Search;

public interface ISearchRepository
{
    Task<SearchResultsDto> SearchAsync(string? q, string? type, int? limit, CancellationToken cancellationToken = default);
}

internal sealed class SearchRepository(IQueryHandler<SearchQuery, SearchResultsDto> search) : ISearchRepository
{
    public Task<SearchResultsDto> SearchAsync(string? q, string? type, int? limit, CancellationToken cancellationToken = default) =>
        search.Handle(new SearchQuery(q, type, limit), cancellationToken);
}
