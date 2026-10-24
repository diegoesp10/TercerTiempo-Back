using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Common;
using TercerTiempo.Domain.Common.Errors;

namespace TercerTiempo.Application.Jobs;

public static class JobNames
{
    public const string FootballData = "football-data";
    public const string OfficialSheets = "official-sheets";
    public const string LicenseRenewals = "license-renewals";
}

public static class JobRunStatus
{
    public const string Running = "running";
    public const string Succeeded = "succeeded";
    public const string Partial = "partial";
    public const string Failed = "failed";
    public const string Paused = "paused";
    public const string Interrupted = "interrupted";
}

public sealed record JobsOverviewDto(
    IReadOnlyList<JobDto> Jobs,
    IReadOnlyList<ScrapeSourceDto> Sources,
    IReadOnlyList<JobRunDto> Runs,
    IReadOnlyList<ScrapeRequestDto> Requests,
    DateTimeOffset GeneratedAt);

public sealed record JobDto(
    string Name,
    string Title,
    string Description,
    int? IntervalMinutes,
    bool Enabled,
    bool Running,
    DateTimeOffset? NextRunAt,
    JobProgressDto? Progress,
    JobRunDto? LastRun);

public sealed record JobProgressDto(string? Current, int Done, int Total);

public sealed record JobRunDto(
    Guid Id,
    string Job,
    bool Manual,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    string? Summary,
    IReadOnlyList<string> Errors,
    int Requests);

// Contadores del control de peticiones de cada web (hora y día en curso) y lo que se ha llamado en las últimas 24 h.
public sealed record ScrapeSourceDto(
    string Provider,
    int? HourRequests,
    int? MaxPerHour,
    int? DayRequests,
    int? MaxPerDay,
    DateTimeOffset? PausedUntil,
    DateTimeOffset? NextRequestAt,
    int Last24h,
    int Failed24h,
    DateTimeOffset? LastRequestAt);

public sealed record ScrapeRequestDto(
    long Id,
    DateTimeOffset At,
    string Provider,
    string Method,
    string Url,
    int? StatusCode,
    int DurationMs,
    string? Error,
    Guid? JobRunId);

public enum JobLaunchResult
{
    Accepted,
    NotFound,
    AlreadyRunning,
    Disabled,
}

public interface IJobsMonitor
{
    Task<JobsOverviewDto> GetOverviewAsync(string? provider, int requests, CancellationToken cancellationToken);
    JobLaunchResult Launch(string job);
}

public sealed record GetJobsOverviewQuery(string? Provider = null, int Requests = 200) : IQuery<JobsOverviewDto>;

internal sealed class GetJobsOverviewHandler(IJobsMonitor monitor) : IQueryHandler<GetJobsOverviewQuery, JobsOverviewDto>
{
    public Task<JobsOverviewDto> Handle(GetJobsOverviewQuery query, CancellationToken cancellationToken) =>
        monitor.GetOverviewAsync(string.IsNullOrWhiteSpace(query.Provider) ? null : query.Provider.Trim(), Math.Clamp(query.Requests, 1, 1000), cancellationToken);
}

public sealed record RunJobCommand(string Job) : ICommand<JobDto>;

internal sealed class RunJobHandler(IJobsMonitor monitor) : ICommandHandler<RunJobCommand, JobDto>
{
    public async Task<JobDto> Handle(RunJobCommand command, CancellationToken cancellationToken)
    {
        switch (monitor.Launch(command.Job))
        {
            case JobLaunchResult.NotFound: throw new NotFoundException(ErrorCode.JobNotFound, command.Job);
            case JobLaunchResult.AlreadyRunning: throw new ConflictException(ErrorCode.JobAlreadyRunning, command.Job);
            case JobLaunchResult.Disabled: throw new ConflictException(ErrorCode.JobDisabled, command.Job);
        }

        var overview = await monitor.GetOverviewAsync(null, 1, cancellationToken);
        return overview.Jobs.Single(j => j.Name == command.Job);
    }
}
