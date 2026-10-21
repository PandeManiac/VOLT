namespace Volt.Core.Timing;

/// <summary>
/// Accumulates elapsed time in exact integer units scaled by the fixed
/// update rate.
/// </summary>
/// <remarks>
/// <para>
/// One fixed step is <see cref="UnitsPerStep"/> units. A real
/// nanosecond of elapsed time adds
/// <c>fixedUpdatesPerSecond</c> units, so a step is consumed exactly
/// when <c>elapsed seconds * rate</c> reaches one, with no repeated
/// rounding of a 16.666667 ms step. The remainder is kept as an
/// integer, which is what makes the interpolation alpha exact and
/// keeps the phase stable over arbitrarily long runs.
/// </para>
/// <para>
/// Accepted elapsed intervals are bounded by
/// <see cref="GameLoopSettings.MaximumFrameDelta"/>, so the product of
/// an interval and a supported rate cannot approach
/// <see cref="long.MaxValue"/> and the accumulator cannot overflow.
/// </para>
/// <para>
/// The type is an implementation detail of <see cref="GameLoop"/>; it
/// is internal so its arithmetic can be tested directly.
/// </para>
/// </remarks>
internal struct FixedStepAccumulator
{
	/// <summary>
	/// Units consumed by one fixed step.
	/// </summary>
	public const long UnitsPerStep = 1_000_000_000;

	private long _units;

	/// <summary>
	/// Gets the whole steps that have not been consumed yet.
	/// </summary>
	public readonly long WholeSteps => _units / UnitsPerStep;

	/// <summary>
	/// Gets the consumed fraction of the next step, in
	/// <c>[0, 1)</c>.
	/// </summary>
	public readonly double Alpha => (double)(_units % UnitsPerStep) / UnitsPerStep;

	/// <summary>
	/// Adds elapsed time and returns how many whole steps became due.
	/// </summary>
	/// <param name="elapsedNanoseconds">
	/// A non-negative elapsed interval, in nanoseconds.
	/// </param>
	/// <param name="fixedUpdatesPerSecond">
	/// The validated fixed simulation rate.
	/// </param>
	/// <returns>The number of whole fixed steps now due.</returns>
	/// <remarks>
	/// The returned count can exceed any policy limit; the caller
	/// decides how many steps to execute and drops the rest. The
	/// fractional remainder is always preserved.
	/// </remarks>
	public long Accumulate(
		long elapsedNanoseconds,
		int fixedUpdatesPerSecond)
	{
		if (elapsedNanoseconds <= 0)
		{
			return 0;
		}

		_units += elapsedNanoseconds * fixedUpdatesPerSecond;

		long steps = _units / UnitsPerStep;

		_units -= steps * UnitsPerStep;

		return steps;
	}

	/// <summary>
	/// Discards all accumulated time, including the fraction.
	/// </summary>
	public void Reset()
	{
		_units = 0;
	}
}
