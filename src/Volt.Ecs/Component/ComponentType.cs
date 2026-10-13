
namespace Volt.Ecs.Component;

/// <summary>
/// Uniquely identifies a registered component type.
/// </summary>
/// <remarks>
/// Component type identifiers are assigned by the component
/// registry and remain stable for the lifetime of the process.
/// Zero is reserved as an invalid identifier.
/// </remarks>
public readonly struct ComponentType(uint id) : IEquatable<ComponentType>, IComparable<ComponentType>
{
    /// <summary>
    /// Gets the numeric identifier of this component type.
    /// </summary>
    public uint Id { get; } = id;

    /// <summary>
    /// Gets whether this identifier is valid.
    /// </summary>
    public bool IsValid => Id != 0;

    public bool Equals(ComponentType other)
    {
        return Id == other.Id;
    }

    public override bool Equals(object? obj)
    {
        return obj is ComponentType other && Equals(other);
    }

    public override int GetHashCode()
    {
        return Id.GetHashCode();
    }

    public int CompareTo(ComponentType other)
    {
        return Id.CompareTo(other.Id);
    }

    public override string ToString()
    {
        return $"ComponentType({Id})";
    }

    public static bool operator ==(ComponentType left, ComponentType right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(ComponentType left, ComponentType right)
    {
        return !left.Equals(right);
    }

    public static bool operator <(ComponentType left, ComponentType right)
    {
        return left.CompareTo(right) < 0;
    }

    public static bool operator <=(ComponentType left, ComponentType right)
    {
        return left.CompareTo(right) <= 0;
    }

    public static bool operator >(ComponentType left, ComponentType right)
    {
        return left.CompareTo(right) > 0;
    }

    public static bool operator >=(ComponentType left, ComponentType right)
    {
        return left.CompareTo(right) >= 0;
    }
}
