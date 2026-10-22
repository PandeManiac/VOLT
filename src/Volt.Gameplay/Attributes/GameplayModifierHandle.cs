using Volt.Ecs;

namespace Volt.Gameplay.Attributes;

/// <summary>
/// Identifies one modifier applied to an entity's attribute.
/// </summary>
/// <remarks>
/// <para>
/// A handle carries the entity, the attribute, the modifier slot, and
/// the slot generation. Removing a modifier advances the slot
/// generation, so a handle retained past removal cannot remove
/// whoever later occupies the slot. The entity generation provides the
/// same protection when an entity index is recycled.
/// </para>
/// <para>
/// Handles are meaningful only for the store that issued them. The
/// default value is <see cref="None"/>.
/// </para>
/// </remarks>
public readonly struct GameplayModifierHandle : IEquatable<GameplayModifierHandle>
{
	internal GameplayModifierHandle(
		Entity entity,
		GameplayAttribute attribute,
		int slot,
		uint generation)
	{
		Entity = entity;
		Attribute = attribute;
		Slot = slot;
		Generation = generation;
	}

	/// <summary>
	/// Gets a handle that refers to no modifier.
	/// </summary>
	public static GameplayModifierHandle None => default;

	/// <summary>
	/// Gets whether this value refers to no modifier.
	/// </summary>
	public bool IsNone => Generation == 0;

	/// <summary>
	/// Gets the entity that owns the modifier.
	/// </summary>
	public Entity Entity { get; }

	/// <summary>
	/// Gets the modified attribute.
	/// </summary>
	public GameplayAttribute Attribute { get; }

	/// <summary>
	/// Gets the modifier slot within the attribute.
	/// </summary>
	public int Slot { get; }

	/// <summary>
	/// Gets the generation that distinguishes successive occupants of
	/// the slot.
	/// </summary>
	public uint Generation { get; }

	/// <inheritdoc />
	public bool Equals(GameplayModifierHandle other)
	{
		return Entity == other.Entity
			&& Attribute == other.Attribute
			&& Slot == other.Slot
			&& Generation == other.Generation;
	}

	/// <inheritdoc />
	public override bool Equals(object? obj)
	{
		return obj is GameplayModifierHandle other && Equals(other);
	}

	/// <inheritdoc />
	public override int GetHashCode()
	{
		return HashCode.Combine(Entity, Attribute, Slot, Generation);
	}

	/// <summary>
	/// Reports whether two handles identify the same modifier.
	/// </summary>
	public static bool operator ==(
		GameplayModifierHandle left,
		GameplayModifierHandle right)
	{
		return left.Equals(right);
	}

	/// <summary>
	/// Reports whether two handles identify different modifiers.
	/// </summary>
	public static bool operator !=(
		GameplayModifierHandle left,
		GameplayModifierHandle right)
	{
		return !left.Equals(right);
	}

	/// <inheritdoc />
	public override string ToString()
	{
		return IsNone
			? "GameplayModifierHandle.None"
			: $"GameplayModifierHandle({Attribute}, slot {Slot}, " +
				$"generation {Generation})";
	}
}
