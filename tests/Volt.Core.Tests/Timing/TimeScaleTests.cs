using System.Numerics;
using Volt.Core.Timing;

namespace Volt.Core.Tests.Timing;

public sealed class TimeScaleTests
{
	[Theory]
	[InlineData(0.0f, 0L, 1L)]
	[InlineData(0.25f, 1L, 4L)]
	[InlineData(0.5f, 1L, 2L)]
	[InlineData(1.0f, 1L, 1L)]
	[InlineData(1.5f, 3L, 2L)]
	[InlineData(2.0f, 2L, 1L)]
	[InlineData(4.0f, 4L, 1L)]
	public void CommonScales_HaveExactRationalValues(
		float value,
		long numerator,
		long denominator)
	{
		TimeScale scale = TimeScale.FromSingle(value);

		Assert.Equal(numerator, (long)scale.Numerator);
		Assert.Equal(denominator, (long)scale.Denominator);
		Assert.Equal(value, scale.Value);
	}

	[Fact]
	public void FractionalFloat_KeepsTheExactBinaryValue()
	{
		TimeScale scale = TimeScale.FromSingle(0.1f);

		// 0.1f is not one tenth; it is 13421773 / 2^27. The scale
		// must preserve that value instead of rounding it again.
		Assert.Equal(13421773L, (long)scale.Numerator);
		Assert.Equal(134217728L, (long)scale.Denominator);
		Assert.Equal(0.1f, scale.Value);
	}

	[Fact]
	public void NonPowerOfTwoFraction_IsRepresentedExactly()
	{
		TimeScale scale = TimeScale.FromSingle(0.75f);

		Assert.Equal(3L, (long)scale.Numerator);
		Assert.Equal(4L, (long)scale.Denominator);
		Assert.Equal(0.75f, scale.Value);
	}

	[Fact]
	public void NormalAndZero_HaveTheDocumentedValues()
	{
		Assert.Equal(1.0f, TimeScale.Normal.Value);
		Assert.False(TimeScale.Normal.IsZero);
		Assert.Equal(0.0f, TimeScale.Zero.Value);
		Assert.True(TimeScale.Zero.IsZero);
	}

	[Fact]
	public void DefaultConstruction_IsUninitializedNotZero()
	{
		TimeScale scale = default;

		Assert.False(scale.IsInitialized);
		Assert.False(scale.IsZero);
		Assert.Equal(Int128.Zero, scale.Numerator);
		Assert.Equal(Int128.Zero, scale.Denominator);
		Assert.True(float.IsNaN(scale.Value));
		Assert.Contains("uninitialized", scale.ToString());
		Assert.NotEqual(TimeScale.Zero, scale);
		Assert.NotEqual(TimeScale.Normal, scale);
		Assert.Equal(default(TimeScale), scale);
	}

	[Fact]
	public void Zero_IsAnInitializedZeroRational()
	{
		Assert.True(TimeScale.Zero.IsInitialized);
		Assert.True(TimeScale.Zero.IsZero);
		Assert.Equal(Int128.Zero, TimeScale.Zero.Numerator);
		Assert.Equal(Int128.One, TimeScale.Zero.Denominator);
	}

	[Theory]
	[InlineData(0.0f)]
	[InlineData(0.25f)]
	[InlineData(1.0f)]
	[InlineData(3.0f)]
	[InlineData(4.0f)]
	public void ConstructedScales_SatisfyTheRationalInvariants(float value)
	{
		TimeScale scale = TimeScale.FromSingle(value);

		Assert.True(scale.IsInitialized);
		Assert.True(scale.Denominator > Int128.Zero);
		Assert.True(scale.Numerator >= Int128.Zero);
		Assert.Equal(scale.Numerator == Int128.Zero, scale.IsZero);
		Assert.True(float.IsFinite(scale.Value));
	}

	[Fact]
	public void ImplicitConversion_ValidatesTheValue()
	{
		TimeScale scale = 0.5f;

		Assert.Equal(TimeScale.FromSingle(0.5f), scale);
		Assert.Throws<ArgumentOutOfRangeException>(() => _ = (TimeScale)(-1.0f));
	}

	[Theory]
	[InlineData(-0.001f)]
	[InlineData(-1.0f)]
	[InlineData(-4.0f)]
	public void NegativeScales_AreRejected(float value)
	{
		Assert.Throws<ArgumentOutOfRangeException>(
			() => TimeScale.FromSingle(value));
	}

	[Theory]
	[InlineData(float.NaN)]
	[InlineData(float.PositiveInfinity)]
	[InlineData(float.NegativeInfinity)]
	public void NonFiniteScales_AreRejected(float value)
	{
		Assert.Throws<ArgumentOutOfRangeException>(
			() => TimeScale.FromSingle(value));
	}

	[Theory]
	[InlineData(4.0001f)]
	[InlineData(10.0f)]
	[InlineData(float.MaxValue)]
	public void ScalesAboveTheMaximum_AreRejected(float value)
	{
		Assert.Throws<ArgumentOutOfRangeException>(
			() => TimeScale.FromSingle(value));
	}

	[Fact]
	public void MinimumSupportedScale_IsExactlyTwoToTheMinus103()
	{
		Assert.Equal(
			0x0C00_0000,
			BitConverter.SingleToInt32Bits(TimeScale.MinimumPositive));

		TimeScale scale = TimeScale.FromSingle(TimeScale.MinimumPositive);

		Assert.True(scale.IsInitialized);
		Assert.False(scale.IsZero);
		Assert.Equal((Int128)1, scale.Numerator);
		Assert.Equal(Int128.One << 103, scale.Denominator);
		Assert.Equal(TimeScale.MinimumPositive, scale.Value);
	}

	[Fact]
	public void FloatsBelowTheMinimum_AreRejected()
	{
		Assert.Throws<ArgumentOutOfRangeException>(
			() => TimeScale.FromSingle(
				BitConverter.Int32BitsToSingle(0x0BFF_FFFF)));

		// 2^-126 used to be the declared minimum. It is below the
		// contiguous supported range and is now rejected even though
		// its own reduced denominator would fit.
		Assert.Throws<ArgumentOutOfRangeException>(
			() => TimeScale.FromSingle(
				BitConverter.Int32BitsToSingle(0x0080_0000)));

		Assert.Throws<ArgumentOutOfRangeException>(
			() => TimeScale.FromSingle(float.Epsilon));

		Assert.Throws<ArgumentOutOfRangeException>(
			() => TimeScale.FromSingle(TimeScale.MinimumPositive / 2.0f));
	}

	[Fact]
	public void FloatWithAnUnrepresentableReducedDenominator_IsRejected()
	{
		// The exact value is 8388609 / 2^149; the denominator cannot
		// fit in a positive signed Int128, so it must be rejected
		// rather than stored with a wrapped shift.
		float value = BitConverter.Int32BitsToSingle(0x0080_0001);

		Assert.Throws<ArgumentOutOfRangeException>(
			() => TimeScale.FromSingle(value));
	}

	[Fact]
	public void FloatAdjacentAboveTheMinimum_IsExact()
	{
		float value = BitConverter.Int32BitsToSingle(0x0C00_0001);
		TimeScale scale = TimeScale.FromSingle(value);

		AssertExactRational(scale, value);
		Assert.Equal(value, scale.Value);
	}

	[Fact]
	public void SampledSupportedFloats_AreExactAndWellFormed()
	{
		int[] fixedPatterns =
		[
			0x0C00_0000,
			0x0C00_0001,
			0x0C00_0002,
			0x3DCC_CCCD,
			0x3F00_0000,
			0x3F80_0000,
			0x4040_0000,
			0x4080_0000,
		];

		foreach (int bits in fixedPatterns)
		{
			AssertSupported(bits);
		}

		Random random = new(20261022);

		for (int sample = 0; sample < 2048; sample++)
		{
			AssertSupported(random.Next(0x0C00_0000, 0x4080_0001));
		}
	}

	private static void AssertSupported(int bits)
	{
		float value = BitConverter.Int32BitsToSingle(bits);
		TimeScale scale = TimeScale.FromSingle(value);

		Assert.True(scale.IsInitialized);
		Assert.True(scale.Denominator > Int128.Zero);
		Assert.True(scale.Numerator > Int128.Zero);
		Assert.True(scale.Denominator <= Int128.MaxValue);
		Assert.Equal(value, scale.Value);
		AssertExactRational(scale, value);
	}

	private static void AssertExactRational(TimeScale scale, float value)
	{
		int bits = BitConverter.SingleToInt32Bits(value);
		int biasedExponent = (bits >> 23) & 0xFF;
		int significand = bits & 0x7F_FFFF;
		int exponent;

		if (biasedExponent == 0)
		{
			exponent = -149;
		}
		else
		{
			significand |= 0x80_0000;
			exponent = biasedExponent - 150;
		}

		BigInteger numerator = significand;
		BigInteger denominator = BigInteger.One;

		if (exponent >= 0)
		{
			numerator <<= exponent;
		}
		else
		{
			denominator <<= -exponent;
		}

		BigInteger divisor = BigInteger.GreatestCommonDivisor(
			numerator,
			denominator);

		numerator /= divisor;
		denominator /= divisor;

		Assert.Equal(numerator, (BigInteger)scale.Numerator);
		Assert.Equal(denominator, (BigInteger)scale.Denominator);
	}

	[Fact]
	public void Equality_ComparesTheExactRational()
	{
		Assert.Equal(TimeScale.FromSingle(2.0f), TimeScale.FromSingle(2.0f));
		Assert.NotEqual(TimeScale.FromSingle(2.0f), TimeScale.FromSingle(2.5f));
		Assert.Equal(
			TimeScale.FromSingle(2.0f).GetHashCode(),
			TimeScale.FromSingle(2.0f).GetHashCode());
	}

	[Fact]
	public void ToString_DescribesTheScale()
	{
		Assert.Contains("0.5", TimeScale.FromSingle(0.5f).ToString());
		Assert.Contains("0x", TimeScale.Zero.ToString());
	}
}
