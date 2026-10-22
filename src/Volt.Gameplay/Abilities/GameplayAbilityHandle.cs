using Volt.Ecs;

namespace Volt.Gameplay.Abilities;

/// <summary>
/// Identifies a granted ability by its owner, slot, and generation.
/// </summary>
/// <remarks>
/// A handle is a value, not a reference. Revoking an ability or
/// destroying its owner retires the handle; a later grant that reuses
/// the slot carries a new generation and is not reachable through the
/// old handle.
/// </remarks>
public readonly struct GameplayAbilityHandle : IEquatable<GameplayAbilityHandle>
{
	internal GameplayAbilityHandle(Entity owner, int slot, uint generation)
	{
		Owner = owner;
		Slot = slot;
		Generation = generation;
	}

	/// <summary>
	/// Gets a handle that refers to no ability.
	/// </summary>
	public static GameplayAbilityHandle None => default;

	/// <summary>
	/// Gets whether this handle refers to no ability.
	/// </summary>
	public bool IsNone => Generation == 0;

	/// <summary>
	/// Gets the entity the ability was granted to.
	/// </summary>
	public Entity Owner { get; }

	/// <summary>
	/// Gets the slot the grant occupied.
	/// </summary>
	public int Slot { get; }

	/// <summary>
	/// Gets the generation of the grant in that slot.
	/// </summary>
	public uint Generation { get; }

	/// <inheritdoc />
	public bool Equals(GameplayAbilityHandle other)
	{
		return Owner == other.Owner
			&& Slot == other.Slot
			&& Generation == other.Generation;
	}

	/// <inheritdoc />
	public override bool Equals(object? obj)
	{
		return obj is GameplayAbilityHandle other && Equals(other);
	}

	/// <inheritdoc />
	public override int GetHashCode()
	{
		return HashCode.Combine(Owner, Slot, Generation);
	}

	/// <summary>
	/// Determines whether two handles refer to the same grant.
	/// </summary>
	public static bool operator ==(
		GameplayAbilityHandle left,
		GameplayAbilityHandle right)
	{
		return left.Equals(right);
	}

	/// <summary>
	/// Determines whether two handles refer to different grants.
	/// </summary>
	public static bool operator !=(
		GameplayAbilityHandle left,
		GameplayAbilityHandle right)
	{
		return !left.Equals(right);
	}

	/// <inheritdoc />
	public override string ToString()
	{
		return IsNone
			? "GameplayAbilityHandle(none)"
			: $"GameplayAbilityHandle({Owner}:{Slot}:{Generation})";
	}
}
