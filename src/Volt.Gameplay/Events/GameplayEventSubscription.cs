namespace Volt.Gameplay.Events;

/// <summary>
/// Identifies one subscription by the event system that owns it, its
/// slot, and its generation.
/// </summary>
/// <remarks>
/// <para>
/// A subscription handle is a value. Unsubscribing retires it, and a
/// later subscription that reuses the slot carries a new generation, so
/// an old handle cannot remove somebody else's listener.
/// </para>
/// <para>
/// The system identity is part of the handle because several event
/// systems may exist in one process. A handle is only meaningful to the
/// system that created it.
/// </para>
/// </remarks>
public readonly struct GameplayEventSubscription
	: IEquatable<GameplayEventSubscription>
{
	internal GameplayEventSubscription(
		uint systemId,
		int slot,
		uint generation)
	{
		SystemId = systemId;
		Slot = slot;
		Generation = generation;
	}

	/// <summary>
	/// Gets a handle that refers to no subscription.
	/// </summary>
	public static GameplayEventSubscription None => default;

	/// <summary>
	/// Gets whether this handle refers to no subscription.
	/// </summary>
	public bool IsNone => Generation == 0;

	/// <summary>
	/// Gets the identity of the event system that created the
	/// subscription.
	/// </summary>
	public uint SystemId { get; }

	/// <summary>
	/// Gets the slot the subscription occupied.
	/// </summary>
	public int Slot { get; }

	/// <summary>
	/// Gets the generation of the subscription in that slot.
	/// </summary>
	public uint Generation { get; }

	/// <inheritdoc />
	public bool Equals(GameplayEventSubscription other)
	{
		return SystemId == other.SystemId
			&& Slot == other.Slot
			&& Generation == other.Generation;
	}

	/// <inheritdoc />
	public override bool Equals(object? obj)
	{
		return obj is GameplayEventSubscription other && Equals(other);
	}

	/// <inheritdoc />
	public override int GetHashCode()
	{
		return HashCode.Combine(SystemId, Slot, Generation);
	}

	/// <summary>
	/// Determines whether two handles refer to the same subscription.
	/// </summary>
	public static bool operator ==(
		GameplayEventSubscription left,
		GameplayEventSubscription right)
	{
		return left.Equals(right);
	}

	/// <summary>
	/// Determines whether two handles refer to different subscriptions.
	/// </summary>
	public static bool operator !=(
		GameplayEventSubscription left,
		GameplayEventSubscription right)
	{
		return !left.Equals(right);
	}

	/// <inheritdoc />
	public override string ToString()
	{
		return IsNone
			? "GameplayEventSubscription(none)"
			: $"GameplayEventSubscription(#{SystemId}:{Slot}:{Generation})";
	}
}
