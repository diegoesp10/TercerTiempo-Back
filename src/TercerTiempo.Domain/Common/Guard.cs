namespace TercerTiempo.Domain.Common;

public static class Guard
{
    public static string NotEmpty(string? value, string name, int maxLength = 200)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new DomainException($"'{name}' no puede estar vacío.");

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
            throw new DomainException($"'{name}' no puede superar {maxLength} caracteres.");

        return trimmed;
    }

    public static Guid NotEmpty(Guid value, string name)
    {
        if (value == Guid.Empty)
            throw new DomainException($"'{name}' no puede estar vacío.");

        return value;
    }

    public static int InRange(int value, int min, int max, string name)
    {
        if (value < min || value > max)
            throw new DomainException($"'{name}' debe estar entre {min} y {max}.");

        return value;
    }

    public static int NotNegative(int value, string name)
    {
        if (value < 0)
            throw new DomainException($"'{name}' no puede ser negativo.");

        return value;
    }
}
