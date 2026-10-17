namespace TercerTiempo.Domain.Common;

/// <summary>Base de toda entidad con identidad propia (Guid v7: ordenable, amigable con índices).</summary>
public abstract class Entity
{
    public Guid Id { get; protected set; }

    protected Entity() { }

    protected Entity(Guid id)
    {
        Id = id;
    }

    protected static Guid NewId() => Guid.CreateVersion7();

    public override bool Equals(object? obj)
    {
        if (obj is not Entity other) return false;
        if (ReferenceEquals(this, other)) return true;
        if (GetType() != other.GetType()) return false;
        if (Id == Guid.Empty || other.Id == Guid.Empty) return false;
        return Id == other.Id;
    }

    public override int GetHashCode() => HashCode.Combine(GetType(), Id);
}
