using Volt.Core.Timing;

namespace Volt.Core.Tests.Timing;

public sealed class FixedStepAccumulatorTests
{
	[Fact]
	public void OneSecondAtSixtyHertz_IsExactlySixtySteps()
	{
		FixedStepAccumulator accumulator = new();

		long steps = accumulator.Accumulate(1_000_000_000, 60);

		Assert.Equal(60, steps);
		Assert.Equal(0.0, accumulator.Alpha);
		Assert.Equal(0, accumulator.WholeSteps);
	}

	[Fact]
	public void FractionalStep_IsPreservedExactly()
	{
		FixedStepAccumulator accumulator = new();

		long steps = accumulator.Accumulate(16_666_666, 60);

		Assert.Equal(0, steps);
		Assert.Equal(0.99999996, accumulator.Alpha, 12);

		steps = accumulator.Accumulate(1, 60);

		// 16,666,666 ns + 1 ns per frame reaches exactly one step.
		Assert.Equal(1, steps);
		Assert.Equal(0.00000002, accumulator.Alpha, 12);
	}

	[Fact]
	public void RepeatedNearSteps_DoNotAccumulateRoundingError()
	{
		FixedStepAccumulator accumulator = new();

		long steps = 0;

		for (int index = 0; index < 6000; index++)
		{
			steps += accumulator.Accumulate(16_666_666, 60);
		}

		// 6000 x 16,666,666 ns is 99,999,996,000 ns, which is
		// 5,999.99976 steps. Truncating 1/60 to 16,666,666 ns every
		// frame must lose exactly the accumulated remainder, never
		// more.
		Assert.Equal(5999, steps);
		Assert.Equal(0.99976, accumulator.Alpha, 6);
	}

	[Fact]
	public void WholeSteps_AreNeverRetainedAfterAccumulation()
	{
		FixedStepAccumulator accumulator = new();

		Assert.Equal(6, accumulator.Accumulate(100_000_000, 60));
		Assert.Equal(0, accumulator.WholeSteps);
		Assert.Equal(0.0, accumulator.Alpha);

		Assert.Equal(0, accumulator.Accumulate(10_000_000, 60));
		Assert.Equal(0.6, accumulator.Alpha, 9);

		Assert.Equal(1, accumulator.Accumulate(10_000_000, 60));
		Assert.Equal(0.2, accumulator.Alpha, 9);
	}

	[Fact]
	public void Reset_DiscardsEverything()
	{
		FixedStepAccumulator accumulator = new();

		accumulator.Accumulate(10_000_000, 60);
		accumulator.Reset();

		Assert.Equal(0.0, accumulator.Alpha);
		Assert.Equal(0, accumulator.WholeSteps);
	}

	[Theory]
	[InlineData(1, 1)]
	[InlineData(7, 7)]
	[InlineData(30, 30)]
	[InlineData(60, 60)]
	[InlineData(240, 240)]
	[InlineData(1000, 1000)]
	public void OneSecondAtAnyRate_IsExact(int rate, int expected)
	{
		FixedStepAccumulator accumulator = new();

		Assert.Equal(
			expected,
			accumulator.Accumulate(1_000_000_000, rate));
		Assert.Equal(0.0, accumulator.Alpha);
	}

	[Fact]
	public void NonPositiveElapsedTime_AddsNothing()
	{
		FixedStepAccumulator accumulator = new();

		accumulator.Accumulate(10_000_000, 60);

		double alpha = accumulator.Alpha;

		Assert.Equal(0, accumulator.Accumulate(0, 60));
		Assert.Equal(0, accumulator.Accumulate(-1, 60));
		Assert.Equal(alpha, accumulator.Alpha);

		Assert.Equal(1, accumulator.Accumulate(10_000_000, 60));
	}
}
