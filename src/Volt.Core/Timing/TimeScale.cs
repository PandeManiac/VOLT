using System.Numerics;

namespace Volt.Core.Timing;

/// <summary>
/// Scales the rate at which real elapsed time becomes simulation
/// time.
/// </summary>
/// <remarks>
/// <para>
/// The value is stored as the exact rational number represented by
/// the validated <see cref="float"/> it was created from, not as a
/// floating-point multiplier applied to elapsed time. Half speed is
/// exactly <c>1/2</c>, quarter speed is exactly <c>1/4</c>, one and a
/// half speed is exactly <c>3/2</c>, and an arbitrary value such as
/// <c>0.1f</c> keeps the exact binary value of that <c>float</c>. The
/// loop multiplies each elapsed interval by the numerator in 128-bit
/// arithmetic, divides by the denominator, and carries the
/// sub-nanosecond remainder into the next interval, so scaling cannot
/// drift by repeatedly truncating a fraction.
/// </para>
/// <para>
/// Supported values are <c>0</c> and the interval
/// <c>[<see cref="MinimumPositive"/>, <see cref="Maximum"/>]</c>. Zero
/// freezes simulation time without pausing the loop. Negative,
/// infinite, and NaN values are rejected: reverse simulation is not
/// supported. The positive domain is deliberately contiguous: every
/// float from <c>2^-103</c> to <c>4.0</c> is accepted and every float
/// below <c>2^-103</c> is rejected. The bound exists because an exact
/// reduced denominator must fit in a positive signed 128-bit integer;
/// the largest such denominator is <c>2^126</c>. A float just below
/// <c>2^-103</c> can reduce to <c>1 / 2^127</c>, which does not fit.
/// </para>
/// <para>
/// The type is a value type with no managed references. It is
/// immutable, so a validated scale can be shared between frames and
/// callbacks without defensive copying.
/// </para>
/// <para>
/// <c>default(TimeScale)</c> is deliberately <em>not</em> a valid
/// zero scale: its numerator and denominator are both zero, so it
/// cannot participate in rational arithmetic. Use
/// <see cref="Zero"/> to freeze time. <see cref="IsInitialized"/> is
/// <c>false</c> only for that uninitialized value, and
/// <see cref="GameLoop.TimeScale"/> rejects it.
/// </para>
/// </remarks>
public readonly struct TimeScale : IEquatable<TimeScale>
{
	/// <summary>
	/// Largest supported scale factor.
	/// </summary>
	public const float Maximum = 4.0f;

	/// <summary>
	/// Smallest supported positive scale, exactly <c>2^-103</c>.
	/// </summary>
	/// <remarks>
	/// This is the lowest float for which a contiguous range has an
	/// exact reduced denominator that fits in a positive signed
	/// <see cref="Int128"/>. At <c>2^-103</c> and above, the reduced
	/// denominator is at most <c>2^126</c>; the next lower float can
	/// reduce to <c>1 / 2^127</c>, which overflows. Smaller values are
	/// rejected even when an individual value (such as
	/// <c>2^-126</c>) would fit, so the accepted domain stays
	/// contiguous and documented by one bound.
	/// </remarks>
	public const float MinimumPositive = 9.8607613E-32f;

	private readonly Int128 _numerator;
	private readonly Int128 _denominator;

	private TimeScale(Int128 numerator, Int128 denominator)
	{
		_numerator = numerator;
		_denominator = denominator;
	}

	/// <summary>
	/// Gets normal speed, <c>1.0x</c>.
	/// </summary>
	public static TimeScale Normal => new(Int128.One, Int128.One);

	/// <summary>
	/// Gets frozen simulation time, <c>0.0x</c>.
	/// </summary>
	public static TimeScale Zero => new(Int128.Zero, Int128.One);

	/// <summary>
	/// Gets whether this is a constructed scale rather than
	/// <c>default(TimeScale)</c>.
	/// </summary>
	/// <remarks>
	/// A constructed scale always has a strictly positive
	/// denominator. The default value has a zero numerator and a zero
	/// denominator and is rejected wherever a scale is consumed.
	/// </remarks>
	public bool IsInitialized => _denominator > Int128.Zero;

	/// <summary>
	/// Gets whether this scale freezes simulation time.
	/// </summary>
	/// <remarks>
	/// <c>default(TimeScale)</c> is not zero; it is uninitialized and
	/// therefore returns <c>false</c>. Compare against
	/// <see cref="Zero"/> instead of a zero numerator.
	/// </remarks>
	public bool IsZero => IsInitialized && _numerator == Int128.Zero;

	/// <summary>
	/// Gets the exact numerator of the scale factor.
	/// </summary>
	/// <remarks>
	/// A constructed scale has a non-negative numerator. The default
	/// value has a zero numerator.
	/// </remarks>
	public Int128 Numerator => _numerator;

	/// <summary>
	/// Gets the exact denominator of the scale factor.
	/// </summary>
	/// <remarks>
	/// A constructed scale has a strictly positive denominator. The
	/// default value has a zero denominator, which is why it must not
	/// be used for arithmetic.
	/// </remarks>
	public Int128 Denominator => _denominator;

	/// <summary>
	/// Gets the scale factor rounded to <see cref="float"/>, for
	/// reporting only.
	/// </summary>
	/// <remarks>
	/// Returns <see cref="float.NaN"/> for <c>default(TimeScale)</c>,
	/// because an uninitialized scale has no numeric value.
	/// </remarks>
	public float Value => !IsInitialized
		? float.NaN
		: _numerator == Int128.Zero
			? 0.0f
			: (float)((double)_numerator / (double)_denominator);

	/// <summary>
	/// Creates a scale from a validated factor.
	/// </summary>
	/// <param name="value">
	/// The factor. It must be finite, non-negative, and in the range
	/// <c>[MinimumPositive, Maximum]</c> when positive.
	/// </param>
	/// <returns>The exact rational value of <paramref name="value"/>.</returns>
	/// <exception cref="ArgumentOutOfRangeException">
	/// The value is NaN, infinite, negative, above
	/// <see cref="Maximum"/>, or below <see cref="MinimumPositive"/>.
	/// </exception>
	public static TimeScale FromSingle(float value)
	{
		if (!float.IsFinite(value))
		{
			throw new ArgumentOutOfRangeException(
				nameof(value),
				value,
				"A time scale must be finite. NaN and infinite scales " +
				"have no deterministic meaning.");
		}

		if (value < 0.0f)
		{
			throw new ArgumentOutOfRangeException(
				nameof(value),
				value,
				"A time scale cannot be negative. Reverse simulation " +
				"is not supported; use zero to freeze time.");
		}

		if (value > Maximum)
		{
			throw new ArgumentOutOfRangeException(
				nameof(value),
				value,
				$"A time scale cannot exceed {Maximum}x.");
		}

		if (value == 0.0f)
		{
			return Zero;
		}

		if (value < MinimumPositive)
		{
			throw new ArgumentOutOfRangeException(
				nameof(value),
				value,
				$"A positive time scale below {MinimumPositive} " +
				$"(2^-103) cannot be represented as an exact reduced " +
				"rational in signed 128-bit integers. Use zero to " +
				"freeze time.");
		}

		// Decompose the float into significand * 2^exponent. Every
		// positive normal float has an exact form, so the rational
		// below reproduces the caller's value without further
		// rounding.
		int bits = BitConverter.SingleToInt32Bits(value);
		int biasedExponent = (bits >> 23) & 0xFF;
		long significand = bits & 0x7F_FFFF;

		if (biasedExponent == 0)
		{
			biasedExponent = -149;
		}
		else
		{
			significand |= 0x80_0000;
			biasedExponent -= 150;
		}

		// Remove trailing zero bits so common values such as 4.0f
		// reduce to an integer numerator with denominator one.
		if (significand != 0)
		{
			int trailing = BitOperations.TrailingZeroCount(
				(ulong)significand);

			significand >>= trailing;
			biasedExponent += trailing;
		}

		// The validated lower bound guarantees a reduced
		// denominator of at most 2^126. This guard keeps the shift
		// safe if the bound or the decomposition ever changes:
		// Int128 shifts do not throw on a count that overflows the
		// value, they quietly produce a wrong rational.
		if (biasedExponent < -126)
		{
			throw new ArgumentOutOfRangeException(
				nameof(value),
				value,
				"The exact reduced denominator of this scale does " +
				"not fit in a positive signed 128-bit integer.");
		}

		Int128 numerator = significand;
		Int128 denominator = Int128.One;

		if (biasedExponent >= 0)
		{
			numerator <<= biasedExponent;
		}
		else
		{
			denominator <<= -biasedExponent;
		}

		return new TimeScale(numerator, denominator);
	}

	/// <inheritdoc />
	public bool Equals(TimeScale other)
	{
		return _numerator == other._numerator &&
			_denominator == other._denominator;
	}

	/// <inheritdoc />
	public override bool Equals(object? obj)
	{
		return obj is TimeScale other && Equals(other);
	}

	/// <inheritdoc />
	public override int GetHashCode()
	{
		return HashCode.Combine(_numerator, _denominator);
	}

	/// <summary>
	/// Converts a factor to a validated scale.
	/// </summary>
	/// <param name="value">The factor.</param>
	/// <returns>The exact rational scale.</returns>
	/// <exception cref="ArgumentOutOfRangeException">
	/// The value is outside the supported range.
	/// </exception>
	/// <remarks>
	/// The implicit conversion exists so an application can write
	/// <c>loop.TimeScale = 0.5f</c>. Validation happens at the
	/// assignment, before the loop observes the value.
	/// </remarks>
	public static implicit operator TimeScale(float value)
	{
		return FromSingle(value);
	}

	/// <summary>
	/// Reports whether two scales are equal.
	/// </summary>
	public static bool operator ==(TimeScale left, TimeScale right)
	{
		return left.Equals(right);
	}

	/// <summary>
	/// Reports whether two scales differ.
	/// </summary>
	public static bool operator !=(TimeScale left, TimeScale right)
	{
		return !left.Equals(right);
	}

	/// <inheritdoc />
	public override string ToString()
	{
		if (!IsInitialized)
		{
			return "TimeScale(uninitialized)";
		}

		return _numerator == Int128.Zero
			? "TimeScale(0x)"
			: $"TimeScale({_numerator}/{_denominator} = {Value}x)";
	}
}
