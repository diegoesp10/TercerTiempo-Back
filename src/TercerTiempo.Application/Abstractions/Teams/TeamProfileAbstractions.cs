using TercerTiempo.Domain.Teams;

namespace TercerTiempo.Application.Abstractions.Teams;

/// <summary>Fuente de la ficha de los clubes (la web de la federación): sede, campo, equipación y escudo.</summary>
public interface ITeamProfileProvider
{
    string ProviderName { get; }

    /// <summary>Ficha del equipo con ese código; null si la página no existe.</summary>
    Task<OfficialTeamProfile?> GetProfileAsync(string teamCode, CancellationToken cancellationToken);

    /// <summary>Descarga el escudo; null si no está disponible.</summary>
    Task<CrestImage?> DownloadCrestAsync(string crestUrl, CancellationToken cancellationToken);

    /// <summary>Página pública del equipo en el proveedor.</summary>
    string TeamUrl(string teamCode);
}

public sealed record OfficialTeamProfile(string? CrestUrl, TeamProfile Profile);

public sealed record CrestImage(byte[] Content, string ContentType);

/// <summary>Escudos guardados en nuestro propio almacenamiento (sin enlazar imágenes de terceros).</summary>
public interface ICrestStorage
{
    /// <summary>Guarda (o sustituye) el escudo del equipo y devuelve su clave.</summary>
    Task<string> SaveAsync(Guid teamId, CrestImage image, CancellationToken cancellationToken);

    /// <summary>Abre el escudo guardado; null si ya no está.</summary>
    Task<CrestFile?> OpenReadAsync(string crestKey, CancellationToken cancellationToken);
}

public sealed record CrestFile(Stream Content, string ContentType);

/// <summary>Descarga una imagen de escudo de cualquier URL pública (el proveedor de datos, la web del club...).</summary>
public interface ICrestDownloader
{
    /// <summary>Null si la URL no responde o no es una imagen.</summary>
    Task<CrestImage?> DownloadAsync(string url, CancellationToken cancellationToken);
}

public sealed class CrestOptions
{
    public const string SectionName = "Crests";

    /// <summary>Carpeta donde se guardan los escudos (relativa al directorio de trabajo o absoluta).</summary>
    public string Directory { get; set; } = "data/crests";

    /// <summary>Días entre consultas de la ficha de un mismo equipo en la federación.</summary>
    public int RefreshDays { get; set; } = 30;
}
