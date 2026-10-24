using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Privacy;

namespace TercerTiempo.Repository.Privacy;

public interface IPrivacyRepository
{
    Task<CookiePolicyDto> GetCookiePolicyAsync(CancellationToken cancellationToken = default);
    Task<ConsentDto> RecordConsentAsync(RecordConsentCommand command, CancellationToken cancellationToken = default);
    Task<TrackResultDto> TrackAsync(TrackEventsCommand command, CancellationToken cancellationToken = default);
}

internal sealed class PrivacyRepository(
    IQueryHandler<GetCookiePolicyQuery, CookiePolicyDto> policy,
    ICommandHandler<RecordConsentCommand, ConsentDto> consent,
    ICommandHandler<TrackEventsCommand, TrackResultDto> track) : IPrivacyRepository
{
    public Task<CookiePolicyDto> GetCookiePolicyAsync(CancellationToken cancellationToken = default) =>
        policy.Handle(new GetCookiePolicyQuery(), cancellationToken);

    public Task<ConsentDto> RecordConsentAsync(RecordConsentCommand command, CancellationToken cancellationToken = default) =>
        consent.Handle(command, cancellationToken);

    public Task<TrackResultDto> TrackAsync(TrackEventsCommand command, CancellationToken cancellationToken = default) =>
        track.Handle(command, cancellationToken);
}
