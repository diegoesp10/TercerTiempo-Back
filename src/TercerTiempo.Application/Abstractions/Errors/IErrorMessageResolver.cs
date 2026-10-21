using TercerTiempo.Domain.Common.Errors;

namespace TercerTiempo.Application.Abstractions.Errors;

/// <summary>Resuelve el texto de un código de error en el idioma pedido a partir de la tabla ErrorMessages.</summary>
public interface IErrorMessageResolver
{
    /// <summary>
    /// Mensaje para el código en ese idioma; si no existe en ese idioma se usa el español y, si tampoco está en la
    /// base de datos, la plantilla embebida del catálogo. Los marcadores {0}, {1}... se rellenan con los argumentos.
    /// </summary>
    Task<string> ResolveAsync(string code, string language, object[] args, CancellationToken cancellationToken = default);
}

internal sealed class ErrorMessageResolver(IErrorMessageStore store) : IErrorMessageResolver
{
    public async Task<string> ResolveAsync(string code, string language, object[] args, CancellationToken cancellationToken = default)
    {
        var messages = await store.GetAllAsync(cancellationToken);

        if (!messages.TryGetValue((code, language), out var template)
            && !messages.TryGetValue((code, ErrorMessage.DefaultLanguage), out template))
        {
            template = ErrorCatalog.Template(code);
        }

        return ErrorCatalog.Fill(template, args);
    }
}
