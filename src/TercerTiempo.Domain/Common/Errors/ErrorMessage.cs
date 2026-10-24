namespace TercerTiempo.Domain.Common.Errors;

public sealed class ErrorMessage
{
    public const string DefaultLanguage = "es";

    public string Code { get; private set; } = string.Empty;

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

public interface IErrorMessageStore
{
    Task<IReadOnlyDictionary<(string Code, string Language), string>> GetAllAsync(CancellationToken cancellationToken = default);
}
