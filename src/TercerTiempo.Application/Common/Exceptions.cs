namespace TercerTiempo.Application.Common;

public sealed class NotFoundException(string entity, object key) : Exception($"{entity} '{key}' no encontrado.")
{
    public string Entity { get; } = entity;
    public object Key { get; } = key;
}

public sealed class ConflictException(string message) : Exception(message);

public sealed class ForbiddenException(string message) : Exception(message);

public sealed class UnauthenticatedException() : Exception("Debes iniciar sesión para realizar esta acción.");

public sealed class InvalidCredentialsException() : Exception("Email o contraseña incorrectos.");
