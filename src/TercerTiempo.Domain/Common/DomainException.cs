using TercerTiempo.Domain.Common.Errors;

namespace TercerTiempo.Domain.Common;

public abstract class CodedException(string code, params object[] args) : Exception(ErrorCatalog.Format(code, args))
{
    public string Code { get; } = code;
    public object[] Args { get; } = args;
}

public class DomainException(string code, params object[] args) : CodedException(code, args);
