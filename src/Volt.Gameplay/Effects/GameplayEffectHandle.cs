using Volt.Ecs;

namespace Volt.Gameplay.Effects;

/// <summary>
/// Identifies one active gameplay effect instance on one entity.
/// </summary>
/// <remarks>
/// <para>
/// A handle carries the target, the instance slot, and the slot
/// generation. Removing an effect advances the slot generation, so a
/// handle retained past removal cannot remove whoever later occupies
/// the slot. The target's generation provides the same protection when
/// an entity index is recycled.
/// </para>
/// <para>
/// Instant effects are never tracked and never produce a handle; the
/// default value is <see cref="None"/>.
/// </para>
/// </remarks>
public readonly struct GameplayEffectHandle : IEquatable<GameplayEffectHandle>
{
	internal GameplayEffectHandle(
		uint systemId,
		Entity target,
		int slot,
		uint generation)
	{
		SystemId = systemId;
		Target = target;
		Slot = slot;
		Generation = generation;
	}

	/// <summary>
	/// Gets a handle that refers to no active effect.
	/// </summary>
	public static GameplayEffectHandle None => default;

	/// <summary>
	/// Gets whether this handle refers to no active effect.
	/// </summary>
	public bool IsNone => Generation == 0;

	/// <summary>
	/// Gets the entity the effect is active on.
	/// </summary>
	public uint SystemId { get; }

	/// <summary>
	/// Gets the entity the effect is active on.
	/// </summary>
	public Entity Target { get; }

	/// <summary>
	/// Gets the instance slot.
	/// </summary>
	public int Slot { get; }

	/// <summary>
	/// Gets the generation that distinguishes successive occupants of
	/// the slot.
	/// </summary>
	public uint Generation { get; }

	/// <inheritdoc />
	public bool Equals(GameplayEffectHandle other)
	{
		return SystemId == other.SystemId
			&& Target == other.Target
			&& Slot == other.Slot
			&& Generation == other.Generation;
	}

	/// <inheritdoc />
	public override bool Equals(object? obj)
	{
		return obj is GameplayEffectHandle other && Equals(other);
	}

	/// <inheritdoc />
	public override int GetHashCode()
	{
		return HashCode.Combine(SystemId, Target, Slot, Generation);
	}

	/// <summary>
	/// Reports whether two handles identify the same instance.
	/// </summary>
	public static bool operator ==(
		GameplayEffectHandle left,
		GameplayEffectHandle right)
	{
		return left.Equals(right);
	}

	/// <summary>
	/// Reports whether two handles identify different instances.
	/// </summary>
	public static bool operator !=(
		GameplayEffectHandle left,
		GameplayEffectHandle right)
	{
		return !left.Equals(right);
	}

	/// <inheritdoc />
	public override string ToString()
	{
		return IsNone
			? "GameplayEffectHandle.None"
			: $"GameplayEffectHandle(#{SystemId}:{Target}, slot {Slot}, " +
				$"generation {Generation})";
	}
}
