namespace TercerTiempo.Domain.Common.Errors;

/// <summary>
/// Mensaje de error en un idioma, guardado en la tabla ErrorMessages. La clave es (código, idioma); la plantilla usa
/// marcadores posicionales ({0}, {1}...) que se rellenan con los argumentos de la excepción.
/// </summary>
public sealed class ErrorMessage
{
    public const string DefaultLanguage = "es";

    public string Code { get; private set; } = string.Empty;

    /// <summary>Código de idioma ISO 639-1 ("es", "en"...).</summary>
    public string Language { get; private set; } = DefaultLanguage;

    public string Message { get; private set; } = string.Empty;

    private ErrorMessage() { }

    public ErrorMessage(string code, string language, string message)
    {
        Code = code;
        Language = language;
        Message = message;
    }
}

/// <summary>Acceso a los mensajes de error guardados en la base de datos.</summary>
public interface IErrorMessageStore
{
    /// <summary>Todos los mensajes, indexados por (código, idioma). Se cachea: cambia poco y se consulta en cada error.</summary>
    Task<IReadOnlyDictionary<(string Code, string Language), string>> GetAllAsync(CancellationToken cancellationToken = default);
}
