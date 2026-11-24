using TercerTiempo.Domain.Sanctions;

namespace TercerTiempo.Application.Tests;

// Lo publicado por el comité de competición, en memoria (en la base de datos lo trae la descarga de la federación).
internal sealed class FakeCommitteeStore : ICommitteeSanctionStore
{
    public List<CommitteeResolution> Resolutions { get; } = [];
    public List<CommitteeSanctionCheck> Checks { get; } = [];

    public Task<IReadOnlyList<CommitteeResolution>> GetBySeasonAsync(Guid seasonId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<CommitteeResolution>>(Resolutions.Where(r => r.SeasonId == seasonId).ToList());

    public Task<IReadOnlyList<CommitteeResolution>> GetStoredBySeasonAsync(Guid seasonId, CancellationToken cancellationToken = default) =>
        GetBySeasonAsync(seasonId, cancellationToken);

    public Task<IReadOnlyList<Guid>> GetSeasonsWithMeetingsSinceAsync(DateOnly since, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Guid>>(Resolutions.Where(r => r.MeetingOn >= since).Select(r => r.SeasonId).Distinct().ToList());

    public Task<IReadOnlyList<CommitteeSanctionCheck>> GetChecksAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<CommitteeSanctionCheck>>(Checks.ToList());

    public void Add(CommitteeResolution resolution) => Resolutions.Add(resolution);

    public void Remove(CommitteeResolution resolution) => Resolutions.Remove(resolution);

    public void AddCheck(CommitteeSanctionCheck check) => Checks.Add(check);

    public CommitteeResolution Publish(Guid seasonId, DateOnly meetingOn, int matchday, params CommitteeSanctionData[] sanctions)
    {
        var resolution = CommitteeResolution.Create(
            seasonId, "test", "Comité Territorial de Competición", meetingOn, matchday, null, sanctions, new DateTimeOffset(meetingOn, new TimeOnly(18, 0), TimeSpan.Zero));
        Resolutions.Add(resolution);
        return resolution;
    }
}
