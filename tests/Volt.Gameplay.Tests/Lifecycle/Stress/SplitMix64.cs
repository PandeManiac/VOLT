namespace Volt.Gameplay.Tests.Lifecycle.Stress;

/// <summary>
/// A tiny deterministic generator whose sequence is defined by this
/// file rather than by a framework version.
/// </summary>
/// <remarks>
/// Replays must produce the same operation stream on every runtime, so
/// the harness cannot depend on <see cref="Random"/> or on hash-order
/// enumeration. SplitMix64 is sufficient for test-data generation and
/// has no allocation or global state.
/// </remarks>
internal struct SplitMix64
{
	private ulong _state;

	public SplitMix64(ulong seed)
	{
		_state = seed;
	}

	public ulong NextUInt64()
	{
		ulong z = _state += 0x9E3779B97F4A7C15UL;

		z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
		z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;

		return z ^ (z >> 31);
	}

	public int NextInt(int exclusiveMaximum)
	{
		if (exclusiveMaximum <= 0)
		{
			throw new ArgumentOutOfRangeException(
				nameof(exclusiveMaximum));
		}

		return (int)(NextUInt64() % (uint)exclusiveMaximum);
	}

	public bool Chance(int percent)
	{
		return NextInt(100) < percent;
	}

	public T Pick<T>(IReadOnlyList<T> values)
	{
		if (values.Count == 0)
		{
			throw new ArgumentException(
				"Cannot pick from an empty collection.",
				nameof(values));
		}

		return values[NextInt(values.Count)];
	}
}
