using TercerTiempo.Domain.Common.Errors;

namespace TercerTiempo.Application.Abstractions.Errors;

public interface IErrorMessageResolver
{
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
