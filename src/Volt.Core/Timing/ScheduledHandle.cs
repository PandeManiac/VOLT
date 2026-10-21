namespace Volt.Core.Timing;

/// <summary>
/// Identifies one registration in a
/// <see cref="SimulationScheduler"/>.
/// </summary>
/// <remarks>
/// <para>
/// A handle carries a slot and a generation. Cancelling frees the slot
/// for reuse and advances the generation, so a handle retained past
/// cancellation cannot affect whatever registration later occupies
/// the same slot. This is what keeps a stale handle harmless without
/// leaking the cancelled entry.
/// </para>
/// <para>
/// Handles are meaningful only for the scheduler that produced them;
/// a handle from another scheduler with the same slot and generation
/// is not detected. The default value is <see cref="None"/>.
/// </para>
/// </remarks>
public readonly struct ScheduledHandle : IEquatable<ScheduledHandle>
{
	internal ScheduledHandle(int slot, uint generation)
	{
		Slot = slot;
		Generation = generation;
	}

	/// <summary>
	/// Gets a handle that refers to no registration.
	/// </summary>
	public static ScheduledHandle None => default;

	/// <summary>
	/// Gets whether this value refers to no registration.
	/// </summary>
	public bool IsNone => Generation == 0;

	internal int Slot { get; }

	internal uint Generation { get; }

	/// <inheritdoc />
	public bool Equals(ScheduledHandle other)
	{
		return Slot == other.Slot &&
			Generation == other.Generation;
	}

	/// <inheritdoc />
	public override bool Equals(object? obj)
	{
		return obj is ScheduledHandle other && Equals(other);
	}

	/// <inheritdoc />
	public override int GetHashCode()
	{
		return HashCode.Combine(Slot, Generation);
	}

	/// <summary>
	/// Reports whether two handles identify the same registration.
	/// </summary>
	public static bool operator ==(
		ScheduledHandle left,
		ScheduledHandle right)
	{
		return left.Equals(right);
	}

	/// <summary>
	/// Reports whether two handles identify different registrations.
	/// </summary>
	public static bool operator !=(
		ScheduledHandle left,
		ScheduledHandle right)
	{
		return !left.Equals(right);
	}

	/// <inheritdoc />
	public override string ToString()
	{
		return IsNone
			? "ScheduledHandle.None"
			: $"ScheduledHandle({Slot}, generation {Generation})";
	}
}
