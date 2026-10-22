
namespace Volt.Ecs;

/// <summary>
/// Identifies an entity by its allocator, slot index,
/// and generation.
/// </summary>
/// <remarks>
/// An entity identifier is a value handle, not a reference.
/// The allocator ID prevents handles issued by different
/// allocators from being treated as interchangeable.
/// </remarks>
public readonly struct Entity(
	uint allocatorId,
	uint index,
	uint generation) : IEquatable<Entity>
{
	/// <summary>
	/// Identifies the allocator that created this entity.
	/// Zero is reserved for invalid entity handles.
	/// </summary>
	public uint AllocatorId { get; } = allocatorId;

	/// <summary>
	/// Identifies the entity's slot within its allocator.
	/// </summary>
	public uint Index { get; } = index;

	/// <summary>
	/// Distinguishes successive occupants of the same slot.
	/// </summary>
	public uint Generation { get; } = generation;

	public bool Equals(Entity other)
	{
		return AllocatorId == other.AllocatorId
			&& Index == other.Index
			&& Generation == other.Generation;
	}

	public override bool Equals(object? obj)
	{
		return obj is Entity other && Equals(other);
	}

	public override int GetHashCode()
	{
		return HashCode.Combine(
			AllocatorId,
			Index,
			Generation);
	}

	public override string ToString()
	{
		return $"Entity({AllocatorId}:{Index}:{Generation})";
	}

	public static bool operator ==(Entity left, Entity right)
	{
		return left.Equals(right);
	}

	public static bool operator !=(Entity left, Entity right)
	{
		return !left.Equals(right);
	}
}
