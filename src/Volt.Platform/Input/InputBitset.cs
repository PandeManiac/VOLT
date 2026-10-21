namespace Volt.Platform.Input;

/// <summary>
/// A fixed-capacity set of small indices, stored as two 64-bit words.
/// </summary>
/// <remarks>
/// <para>
/// The set is a value type with no managed references, so it can live
/// inside an input snapshot and be copied with the snapshot. Every
/// index outside the capacity is ignored, which is what makes an
/// unknown or out-of-range key code safe to query.
/// </para>	/// <para>
/// The capacity is larger than the current key and button counts with
/// room to grow; <c>KeyboardInputTests.KeyCodes_FitTheSnapshotBitset</c>
/// fails if an enum member ever outgrows it.
/// </para>
/// </remarks>
internal readonly struct InputBitset
{
	/// <summary>
	/// Number of indices this set can hold.
	/// </summary>
	internal const int Capacity = 128;

	private const int WordBits = 64;

	private readonly ulong _low;
	private readonly ulong _high;

	private InputBitset(ulong low, ulong high)
	{
		_low = low;
		_high = high;
	}

	/// <summary>
	/// Gets an empty set.
	/// </summary>
	internal static InputBitset Empty => default;

	/// <summary>
	/// Gets whether no index is set.
	/// </summary>
	internal bool IsEmpty => (_low | _high) == 0;

	/// <summary>
	/// Gets whether the index is set. Out-of-range indices are false.
	/// </summary>
	internal bool Contains(int index)
	{
		if ((uint)index >= Capacity)
		{
			return false;
		}

		ulong word = index < WordBits ? _low : _high;
		return (word & Mask(index)) != 0;
	}

	/// <summary>
	/// Returns a set with the index added. Out-of-range indices are
	/// ignored.
	/// </summary>
	internal InputBitset With(int index)
	{
		if ((uint)index >= Capacity)
		{
			return this;
		}

		return index < WordBits
			? new InputBitset(_low | Mask(index), _high)
			: new InputBitset(_low, _high | Mask(index));
	}

	/// <summary>
	/// Returns a set with the index removed. Out-of-range indices are
	/// ignored.
	/// </summary>
	internal InputBitset Without(int index)
	{
		if ((uint)index >= Capacity)
		{
			return this;
		}

		return index < WordBits
			? new InputBitset(_low & ~Mask(index), _high)
			: new InputBitset(_low, _high & ~Mask(index));
	}

	private static ulong Mask(int index)
	{
		return 1UL << (index & (WordBits - 1));
	}
}
