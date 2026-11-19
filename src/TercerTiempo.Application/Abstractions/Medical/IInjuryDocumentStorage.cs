namespace TercerTiempo.Application.Abstractions.Medical;

// Ficheros de las pruebas e informes de las lesiones. No van con los escudos ni las fotos: nunca se sirven en abierto, solo
// por la API, con la sesión y el permiso comprobados en cada descarga.
public interface IInjuryDocumentStorage
{
    Task<string> SaveAsync(Guid injuryId, byte[] content, string contentType, CancellationToken cancellationToken);

    Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken);

    Task DeleteAsync(string key, CancellationToken cancellationToken);
}

public sealed class InjuryDocumentOptions
{
    public const string SectionName = "InjuryDocuments";

    public string Directory { get; set; } = "data/medical";
}
