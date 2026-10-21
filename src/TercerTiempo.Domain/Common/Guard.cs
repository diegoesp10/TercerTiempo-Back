using TercerTiempo.Domain.Common.Errors;

namespace TercerTiempo.Domain.Common;

public static class Guard
{
    public static string NotEmpty(string? value, string name, int maxLength = 200)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new DomainException(ErrorCode.CommonRequired, name);

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
            throw new DomainException(ErrorCode.CommonTooLong, name, maxLength);

        return trimmed;
    }

    public static Guid NotEmpty(Guid value, string name)
    {
        if (value == Guid.Empty)
            throw new DomainException(ErrorCode.CommonRequired, name);

        return value;
    }

    public static int InRange(int value, int min, int max, string name)
    {
        if (value < min || value > max)
            throw new DomainException(ErrorCode.CommonOutOfRange, name, min, max);

        return value;
    }

    public static int NotNegative(int value, string name)
    {
        if (value < 0)
            throw new DomainException(ErrorCode.CommonNegative, name);

        return value;
    }
}
