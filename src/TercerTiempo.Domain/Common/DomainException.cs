using TercerTiempo.Domain.Common.Errors;

namespace TercerTiempo.Domain.Common;

/// <summary>
/// Excepción con código de error del catálogo (<see cref="ErrorCode"/>) y argumentos para el mensaje. El texto que
/// ve el usuario no está aquí: lo resuelve la API por código e idioma desde la tabla ErrorMessages; <see cref="Exception.Message"/>
/// lleva el mensaje por defecto en español (el del catálogo embebido) para logs y tests.
/// </summary>
public abstract class CodedException(string code, params object[] args) : Exception(ErrorCatalog.Format(code, args))
{
    public string Code { get; } = code;
    public object[] Args { get; } = args;
}

/// <summary>Violación de una regla de negocio. La API la traduce a un 422 con ProblemDetails.</summary>
public class DomainException(string code, params object[] args) : CodedException(code, args);
