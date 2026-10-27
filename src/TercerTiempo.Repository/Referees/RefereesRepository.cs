using System.ComponentModel;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Referees;

namespace TercerTiempo.Repository.Referees;

public interface IRefereesRepository
{
    Task<IReadOnlyList<RefereeSummaryDto>> ListAsync(string? search, CancellationToken cancellationToken = default);
    Task<RefereeDetailDto> GetAsync(Guid refereeId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PendingRefereeRatingDto>> GetPendingAsync(CancellationToken cancellationToken = default);
    Task<MatchOfficialsDto> GetMatchOfficialsAsync(Guid matchId, CancellationToken cancellationToken = default);
    Task<MatchOfficialsDto> RateAsync(Guid matchId, Guid refereeId, RefereeRatingRequest request, CancellationToken cancellationToken = default);
}

public sealed record RefereeRatingRequest(
    [property: Description("De 0 a 10.")]
    int Score,
    [property: Description("Opcional: por qué esa nota (hasta 1000 caracteres).")]
    string? Comment,
    [property: Description("Solo si el club tiene los dos equipos del partido: en nombre de cuál puntúa.")]
    Guid? TeamId = null);

internal sealed class RefereesRepository(
    IQueryHandler<ListRefereesQuery, IReadOnlyList<RefereeSummaryDto>> list,
    IQueryHandler<GetRefereeQuery, RefereeDetailDto> get,
    IQueryHandler<ListPendingRefereeRatingsQuery, IReadOnlyList<PendingRefereeRatingDto>> pending,
    IQueryHandler<GetMatchOfficialsQuery, MatchOfficialsDto> officials,
    ICommandHandler<RateRefereeCommand, MatchOfficialsDto> rate) : IRefereesRepository
{
    public Task<IReadOnlyList<RefereeSummaryDto>> ListAsync(string? search, CancellationToken cancellationToken = default) =>
        list.Handle(new ListRefereesQuery(search), cancellationToken);

    public Task<RefereeDetailDto> GetAsync(Guid refereeId, CancellationToken cancellationToken = default) =>
        get.Handle(new GetRefereeQuery(refereeId), cancellationToken);

    public Task<IReadOnlyList<PendingRefereeRatingDto>> GetPendingAsync(CancellationToken cancellationToken = default) =>
        pending.Handle(new ListPendingRefereeRatingsQuery(), cancellationToken);

    public Task<MatchOfficialsDto> GetMatchOfficialsAsync(Guid matchId, CancellationToken cancellationToken = default) =>
        officials.Handle(new GetMatchOfficialsQuery(matchId), cancellationToken);

    public Task<MatchOfficialsDto> RateAsync(Guid matchId, Guid refereeId, RefereeRatingRequest request, CancellationToken cancellationToken = default) =>
        rate.Handle(new RateRefereeCommand(matchId, refereeId, request.Score, request.Comment, request.TeamId), cancellationToken);
}
