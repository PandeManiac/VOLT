
using System.Collections;

namespace Volt.Ecs.Component;

/// <summary>
/// Represents an immutable, canonical set of component types.
/// Used to identify and compare ECS archetypes.
/// </summary>
/// <remarks>
/// Component types are stored in ascending ID order without
/// duplicates. Equality and hashing depend on the complete
/// set of component IDs, not their original ordering.
///
/// Construction allocates an array. Membership checks,
/// comparisons, and hashing do not allocate.
/// </remarks>
public sealed class ComponentSignature :
    IEquatable<ComponentSignature>,
    IReadOnlyList<ComponentType>
{
    private readonly ComponentType[] _types;
    private readonly int _hashCode;

    /// <summary>
    /// Gets the empty component signature.
    /// </summary>
    public static ComponentSignature Empty { get; } = new([], true);

    /// <summary>
    /// Gets the number of distinct component types.
    /// </summary>
    public int Count => _types.Length;

    /// <summary>
    /// Gets the component type at the specified position.
    /// Types are ordered by ascending ID.
    /// </summary>
    public ComponentType this[int index] => _types[index];

    /// <summary>
    /// Creates a canonical signature from component types.
    /// Duplicate types are removed automatically.
    /// </summary>
    public ComponentSignature(params ComponentType[] types)
    {
        ArgumentNullException.ThrowIfNull(types);

        _types = (ComponentType[])types.Clone();
        Array.Sort(_types);

        int uniqueCount = 0;

        for (int i = 0; i < _types.Length; i++)
        {
            ComponentType type = _types[i];

            if (!type.IsValid)
            {
                throw new ArgumentException(
                    "Component signatures cannot contain invalid component types.",
                    nameof(types));
            }

            if (uniqueCount == 0 || _types[uniqueCount - 1] != type)
            {
                _types[uniqueCount++] = type;
            }
        }

        if (uniqueCount != _types.Length)
        {
            Array.Resize(ref _types, uniqueCount);
        }

        _hashCode = ComputeHashCode(_types);
    }

    private ComponentSignature(ComponentType[] types, bool canonical)
    {
        _types = types;
        _hashCode = ComputeHashCode(types);
    }

    /// <summary>
    /// Returns whether the signature contains a component type.
    /// </summary>
    public bool Contains(ComponentType type)
    {
        return Array.BinarySearch(_types, type) >= 0;
    }

    /// <summary>
    /// Returns whether this signature contains every component
    /// type present in another signature.
    /// </summary>
    public bool ContainsAll(ComponentSignature other)
    {
        ArgumentNullException.ThrowIfNull(other);

        int i = 0;
        int j = 0;

        while (i < _types.Length && j < other._types.Length)
        {
            uint left = _types[i].Id;
            uint right = other._types[j].Id;

            if (left < right)
            {
                i++;
            }

            else if (left == right)
            {
                i++;
                j++;
            }

            else
            {
                return false;
            }
        }

        return j == other._types.Length;
    }

    /// <summary>
    /// Returns whether this signature contains at least one
    /// component type present in another signature.
    /// </summary>
    public bool ContainsAny(ComponentSignature other)
    {
        ArgumentNullException.ThrowIfNull(other);

        int i = 0;
        int j = 0;

        while (i < _types.Length && j < other._types.Length)
        {
            uint left = _types[i].Id;
            uint right = other._types[j].Id;

            if (left < right)
            {
                i++;
            }

            else if (left > right)
            {
                j++;
            }

            else
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Creates a new signature containing an additional
    /// component type.
    /// </summary>
    public ComponentSignature With(ComponentType type)
    {
        if (!type.IsValid)
        {
            throw new ArgumentException(
                "Component type must be valid.",
                nameof(type));
        }

        if (Contains(type))
        {
            return this;
        }

        var types = new ComponentType[_types.Length + 1];
        int insertionIndex = ~Array.BinarySearch(_types, type);

        Array.Copy(_types, 0, types, 0, insertionIndex);
        types[insertionIndex] = type;

        Array.Copy(
            _types,
            insertionIndex,
            types,
            insertionIndex + 1,
            _types.Length - insertionIndex);

        return new ComponentSignature(types, true);
    }

    /// <summary>
    /// Creates a new signature without the specified
    /// component type.
    /// </summary>
    public ComponentSignature Without(ComponentType type)
    {
        int index = Array.BinarySearch(_types, type);

        if (index < 0)
        {
            return this;
        }

        var types = new ComponentType[_types.Length - 1];

        Array.Copy(_types, 0, types, 0, index);

        Array.Copy(
            _types,
            index + 1,
            types,
            index,
            _types.Length - index - 1);

        return new ComponentSignature(types, true);
    }

    public bool Equals(ComponentSignature? other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (other is null
            || _hashCode != other._hashCode
            || _types.Length != other._types.Length)
        {
            return false;
        }

        return _types.AsSpan().SequenceEqual(other._types);
    }

    public override bool Equals(object? obj)
    {
        return obj is ComponentSignature other && Equals(other);
    }

    public override int GetHashCode()
    {
        return _hashCode;
    }

    public IEnumerator<ComponentType> GetEnumerator()
    {
        return ((IEnumerable<ComponentType>)_types).GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return _types.GetEnumerator();
    }

    public static bool operator ==(
        ComponentSignature? left,
        ComponentSignature? right)
    {
        return Equals(left, right);
    }

    public static bool operator !=(
        ComponentSignature? left,
        ComponentSignature? right)
    {
        return !Equals(left, right);
    }

    private static int ComputeHashCode(ReadOnlySpan<ComponentType> types)
    {
        const uint offsetBasis = 2166136261;
        const uint prime = 16777619;

        uint hash = offsetBasis;

        foreach (ComponentType type in types)
        {
            hash ^= type.Id;
            hash *= prime;
        }

        hash ^= (uint)types.Length;
        hash *= prime;

        return unchecked((int)hash);
    }
}
