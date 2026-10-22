using TercerTiempo.Domain.Teams;

namespace TercerTiempo.Application.Abstractions.Teams;

public interface ITeamProfileProvider
{
    string ProviderName { get; }

    Task<OfficialTeamProfile?> GetProfileAsync(string teamCode, CancellationToken cancellationToken);

    Task<CrestImage?> DownloadCrestAsync(string crestUrl, CancellationToken cancellationToken);

    string TeamUrl(string teamCode);
}

public sealed record OfficialTeamProfile(string? CrestUrl, TeamProfile Profile);

public sealed record CrestImage(byte[] Content, string ContentType);

public interface ICrestStorage
{
    Task<string> SaveAsync(Guid teamId, CrestImage image, CancellationToken cancellationToken);

    Task<CrestFile?> OpenReadAsync(string crestKey, CancellationToken cancellationToken);
}

public sealed record CrestFile(Stream Content, string ContentType);

public interface ICrestDownloader
{
    Task<CrestImage?> DownloadAsync(string url, CancellationToken cancellationToken);
}

public sealed class CrestOptions
{
    public const string SectionName = "Crests";

    public string Directory { get; set; } = "data/crests";

    public int RefreshDays { get; set; } = 30;
}
