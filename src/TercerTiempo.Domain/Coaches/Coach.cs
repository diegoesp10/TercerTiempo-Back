using TercerTiempo.Domain.Common;

namespace TercerTiempo.Domain.Coaches;

public sealed class Coach : AggregateRoot
{
    public Guid UserId { get; private set; }
    public string DisplayName { get; private set; } = string.Empty;
    public string NormalizedName { get; private set; } = string.Empty;
    public DateTimeOffset RegisteredAt { get; private set; }

    // Foto que sube el propio técnico desde su perfil (nadie más la cambia).
    public string? PhotoKey { get; private set; }

    private Coach() { }

    public static Coach Register(Guid userId, string displayName, DateTimeOffset now)
    {
        var name = Guard.NotEmpty(displayName, "nombre", 100);

        return new Coach
        {
            Id = NewId(),
            UserId = Guard.NotEmpty(userId, nameof(userId)),
            DisplayName = name,
            NormalizedName = NameNormalizer.Normalize(name),
            RegisteredAt = now,
        };
    }

    public void UpdateProfile(string displayName)
    {
        var name = Guard.NotEmpty(displayName, "nombre", 100);
        DisplayName = name;
        NormalizedName = NameNormalizer.Normalize(name);
    }

    public void SetPhoto(string photoKey) => PhotoKey = Guard.NotEmpty(photoKey, nameof(photoKey), 100);

    public void ClearPhoto() => PhotoKey = null;
}
