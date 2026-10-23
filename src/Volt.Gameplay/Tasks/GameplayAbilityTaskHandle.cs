using Volt.Ecs;

namespace Volt.Gameplay.Tasks;

/// <summary>
/// Identifies one pending ability task by its system, owner, slot,
/// generation, and owning activation.
/// </summary>
/// <remarks>
/// <para>
/// The activation identity is part of the handle, not only the grant.
/// A grant can activate repeatedly, so a slot and generation alone
/// cannot tell a task of the previous activation from a task of the
/// current one. A stale handle whose slot was reused by a new task of a
/// later activation therefore fails both the generation and the
/// activation check.
/// </para>
/// <para>
/// Completing or cancelling a task retires its handle. The completion
/// notification reports the transition; it is not necessary to keep a
/// completed task record alive for later inspection.
/// </para>
/// </remarks>
public readonly struct GameplayAbilityTaskHandle
	: IEquatable<GameplayAbilityTaskHandle>
{
	internal GameplayAbilityTaskHandle(
		uint systemId,
		Entity owner,
		int slot,
		uint generation,
		uint activationId)
	{
		SystemId = systemId;
		Owner = owner;
		Slot = slot;
		Generation = generation;
		ActivationId = activationId;
	}

	/// <summary>
	/// Gets a handle that refers to no task.
	/// </summary>
	public static GameplayAbilityTaskHandle None => default;

	/// <summary>
	/// Gets whether this handle refers to no task.
	/// </summary>
	public bool IsNone => Generation == 0;

	/// <summary>
	/// Gets the identity of the task system that created the task.
	/// </summary>
	public uint SystemId { get; }

	/// <summary>
	/// Gets the entity that owns the ability.
	/// </summary>
	public Entity Owner { get; }

	/// <summary>
	/// Gets the slot the task occupied.
	/// </summary>
	public int Slot { get; }

	/// <summary>
	/// Gets the generation of the task in that slot.
	/// </summary>
	public uint Generation { get; }

	/// <summary>
	/// Gets the identity of the activation that owns the task.
	/// </summary>
	public uint ActivationId { get; }

	/// <inheritdoc />
	public bool Equals(GameplayAbilityTaskHandle other)
	{
		return SystemId == other.SystemId
			&& Owner == other.Owner
			&& Slot == other.Slot
			&& Generation == other.Generation
			&& ActivationId == other.ActivationId;
	}

	/// <inheritdoc />
	public override bool Equals(object? obj)
	{
		return obj is GameplayAbilityTaskHandle other && Equals(other);
	}

	/// <inheritdoc />
	public override int GetHashCode()
	{
		return HashCode.Combine(
			SystemId,
			Owner,
			Slot,
			Generation,
			ActivationId);
	}

	/// <summary>
	/// Determines whether two handles refer to the same task.
	/// </summary>
	public static bool operator ==(
		GameplayAbilityTaskHandle left,
		GameplayAbilityTaskHandle right)
	{
		return left.Equals(right);
	}

	/// <summary>
	/// Determines whether two handles refer to different tasks.
	/// </summary>
	public static bool operator !=(
		GameplayAbilityTaskHandle left,
		GameplayAbilityTaskHandle right)
	{
		return !left.Equals(right);
	}

	/// <inheritdoc />
	public override string ToString()
	{
		return IsNone
			? "GameplayAbilityTaskHandle(none)"
			: $"GameplayAbilityTaskHandle(#{SystemId}:{Owner}:{Slot}:{Generation}:A{ActivationId})";
	}
}
