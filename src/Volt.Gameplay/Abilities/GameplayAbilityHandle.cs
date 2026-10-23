using Volt.Ecs;

namespace Volt.Gameplay.Abilities;

/// <summary>
/// Identifies a granted ability by the system that owns it, its owner,
/// its slot, and its generation.
/// </summary>
/// <remarks>
/// <para>
/// A handle is a value, not a reference. Revoking an ability or
/// destroying its owner retires the handle; a later grant that reuses
/// the slot carries a new generation and is not reachable through the
/// old handle.
/// </para>
/// <para>
/// The system identity is part of the handle because two
/// <see cref="GameplayAbilitySystem"/> instances may operate on the same
/// world and therefore allocate the same owner and slot numbers. A
/// handle is only meaningful to the system that created it.
/// </para>
/// </remarks>
public readonly struct GameplayAbilityHandle : IEquatable<GameplayAbilityHandle>
{
	internal GameplayAbilityHandle(
		uint systemId,
		Entity owner,
		int slot,
		uint generation)
	{
		SystemId = systemId;
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
	/// Gets the identity of the system that created the grant.
	/// </summary>
	public uint SystemId { get; }

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
		return SystemId == other.SystemId
			&& Owner == other.Owner
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
		return HashCode.Combine(SystemId, Owner, Slot, Generation);
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
			: $"GameplayAbilityHandle(#{SystemId}:{Owner}:{Slot}:{Generation})";
	}
}
