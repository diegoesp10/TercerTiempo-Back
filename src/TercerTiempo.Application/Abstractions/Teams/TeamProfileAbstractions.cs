namespace TercerTiempo.Application.Abstractions.Teams;

public interface ITeamProfileProvider
{
    string ProviderName { get; }

    string TeamUrl(string teamCode);
}

public sealed record CrestImage(byte[] Content, string ContentType);

public interface ICrestStorage
{
    Task<string> SaveAsync(Guid teamId, CrestImage image, CancellationToken cancellationToken);

    Task<CrestFile?> OpenReadAsync(string crestKey, CancellationToken cancellationToken);
}

public sealed record CrestFile(Stream Content, string ContentType);

public interface IPlayerPhotoStorage
{
    Task<string> SaveAsync(Guid rosterEntryId, CrestImage image, CancellationToken cancellationToken);

    Task<CrestFile?> OpenReadAsync(string photoKey, CancellationToken cancellationToken);
}

public interface ICrestDownloader
{
    Task<CrestImage?> DownloadAsync(string url, CancellationToken cancellationToken);
}

public sealed class CrestOptions
{
    public const string SectionName = "Crests";

    public string Directory { get; set; } = "data/crests";

    public string PhotosDirectory { get; set; } = "data/photos";
}
