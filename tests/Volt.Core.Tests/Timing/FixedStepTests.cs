using Volt.Core.Timing;

namespace Volt.Core.Tests.Timing;

public sealed class FixedStepTests
{
	[Theory]
	[InlineData(16_666_667, 600, 600)]
	[InlineData(33_333_334, 60, 120)]
	[InlineData(6_944_445, 144, 60)]
	[InlineData(4_166_667, 240, 60)]
	public void RenderRate_ProducesTheDocumentedStepCount(
		long frameNanoseconds,
		int frames,
		int expectedSteps)
	{
		GameLoop loop = GameLoopLifecycleTests.CreateLoop(
			out FakeMonotonicClock clock);

		for (int index = 0; index < frames; index++)
		{
			clock.AdvanceNanoseconds(frameNanoseconds);
			loop.Advance();
		}

		Assert.Equal(expectedSteps, loop.TotalFixedSteps);
		Assert.InRange(loop.InterpolationAlpha, 0.0f, 0.9999999f);
	}

	[Fact]
	public void VariableFrameTimes_ProduceTheSameStepCount()
	{
		GameLoop loop = GameLoopLifecycleTests.CreateLoop(
			out FakeMonotonicClock clock);

		for (int index = 0; index < 16; index++)
		{
			clock.AdvanceNanoseconds(62_500_000);
			loop.Advance();
		}

		long stepped = loop.TotalFixedSteps;

		GameLoop other = GameLoopLifecycleTests.CreateLoop(
			out FakeMonotonicClock otherClock);

		for (int index = 0; index < 100; index++)
		{
			otherClock.AdvanceNanoseconds(10_000_000);
			other.Advance();
		}

		Assert.Equal(60, stepped);
		Assert.Equal(stepped, other.TotalFixedSteps);
		Assert.Equal(0.0f, other.InterpolationAlpha);
	}

	[Fact]
	public void LongRun_DoesNotDrift()
	{
		GameLoop loop = GameLoopLifecycleTests.CreateLoop(
			out FakeMonotonicClock clock);

		const int frames = 1_000_000;

		for (int index = 0; index < frames; index++)
		{
			clock.AdvanceNanoseconds(1_000_000);
			loop.Advance();
		}

		// One million milliseconds are exactly one thousand seconds,
		// which are exactly sixty thousand 1/60 second steps.
		Assert.Equal(60_000, loop.TotalFixedSteps);
		Assert.Equal(1000.0, loop.SimulationTimeSeconds, 9);
		Assert.Equal(0.0f, loop.InterpolationAlpha);
		Assert.Equal(TimeSpan.Zero, loop.TotalDroppedTime);
	}

	[Fact]
	public void ElapsedTimeAboveTheClamp_IsDropped()
	{
		GameLoop loop = GameLoopLifecycleTests.CreateLoop(
			out FakeMonotonicClock clock,
			maxFixedStepsPerFrame: 20);

		clock.AdvanceNanoseconds(400_000_000);
		FrameTiming frame = loop.Advance();

		Assert.Equal(TimeSpan.FromMilliseconds(250), frame.RealDelta);
		Assert.Equal(15, frame.FixedSteps);
		Assert.InRange(frame.DroppedTime.TotalMilliseconds, 149.9, 150.1);
		Assert.Equal(loop.TotalDroppedTime, frame.DroppedTime);
	}

	[Fact]
	public void FixedStepCap_DropsTheExcessWithoutBacklog()
	{
		GameLoop loop = GameLoopLifecycleTests.CreateLoop(
			out FakeMonotonicClock clock);

		clock.AdvanceNanoseconds(250_000_000);
		FrameTiming overloaded = loop.Advance();

		Assert.Equal(5, overloaded.FixedSteps);
		Assert.InRange(
			overloaded.DroppedTime.TotalMilliseconds,
			166.6,
			166.7);

		clock.AdvanceNanoseconds(16_666_667);
		FrameTiming quiet = loop.Advance();

		// The dropped backlog does not reappear on the next frame.
		Assert.Equal(1, quiet.FixedSteps);
		Assert.Equal(TimeSpan.Zero, quiet.DroppedTime);
		Assert.Equal(6, loop.TotalFixedSteps);
	}

	[Fact]
	public void Construction_SamplesTheClockWithoutFabricatingADelta()
	{
		FakeMonotonicClock clock = new();

		clock.SetTimestamp(1_000_000_000_000);

		GameLoop loop = new(GameLoopSettings.Default, clock);

		clock.AdvanceNanoseconds(1_000_000);

		FrameTiming frame = loop.Advance();

		// A clock that has already been running for hours must not
		// turn its absolute value into the first frame's delta.
		Assert.Equal(0, frame.FixedSteps);
		Assert.Equal(TimeSpan.FromMilliseconds(1), frame.RealDelta);
	}

	[Fact]
	public void ZeroElapsedTime_RunsNoStep()
	{
		GameLoop loop = GameLoopLifecycleTests.CreateLoop(
			out FakeMonotonicClock clock);

		FrameTiming frame = loop.Advance();

		Assert.Equal(0, frame.FixedSteps);
		Assert.Equal(0.0f, frame.InterpolationAlpha);
		Assert.Equal(TimeSpan.Zero, frame.RealDelta);
	}

	[Fact]
	public void NegativeElapsedTime_IsTreatedAsZero()
	{
		GameLoop loop = GameLoopLifecycleTests.CreateLoop(
			out FakeMonotonicClock clock);

		clock.AdvanceNanoseconds(1_000_000_000);
		loop.Advance();

		clock.AdvanceNanoseconds(-5_000_000);
		FrameTiming frame = loop.Advance();

		Assert.Equal(0, frame.FixedSteps);
		Assert.Equal(TimeSpan.Zero, frame.RealDelta);
		Assert.Equal(5, loop.TotalFixedSteps);
	}

	[Fact]
	public void EnormousElapsedTime_IsClampedWithoutOverflow()
	{
		GameLoop loop = GameLoopLifecycleTests.CreateLoop(
			out FakeMonotonicClock clock);

		clock.AdvanceNanoseconds(long.MaxValue / 2);
		FrameTiming frame = loop.Advance();

		Assert.Equal(5, frame.FixedSteps);
		Assert.Equal(TimeSpan.FromMilliseconds(250), frame.RealDelta);
		Assert.True(frame.DroppedTime.TotalDays > 1000);
		Assert.True(loop.TotalDroppedTime.TotalDays > 1000);
	}

	[Fact]
	public void LargeTimestamps_KeepNanosecondDeltas()
	{
		GameLoop loop = GameLoopLifecycleTests.CreateLoop(
			out FakeMonotonicClock clock);

		clock.SetTimestamp(long.MaxValue / 2);
		loop.ResetTiming();

		clock.AdvanceNanoseconds(16_666_667);
		FrameTiming frame = loop.Advance();

		Assert.Equal(1, frame.FixedSteps);
		Assert.Equal(
			TimeSpan.FromTicks(166_666),
			frame.RealDelta);
	}

	[Fact]
	public void InterpolationAlpha_FollowsTheSubStepRemainder()
	{
		GameLoop loop = GameLoopLifecycleTests.CreateLoop(
			out FakeMonotonicClock clock);

		float[] alphas = new float[5];

		for (int index = 0; index < alphas.Length; index++)
		{
			clock.AdvanceNanoseconds(10_000_000);
			alphas[index] = loop.Advance().InterpolationAlpha;
		}

		Assert.Equal(0.6f, alphas[0], 5);
		Assert.Equal(0.2f, alphas[1], 5);
		Assert.Equal(0.8f, alphas[2], 5);
		Assert.Equal(0.4f, alphas[3], 5);
		Assert.Equal(0.0f, alphas[4], 5);
		Assert.Equal(3, loop.TotalFixedSteps);
	}

	[Fact]
	public void InterpolationAlpha_NeverRoundsUpToOne()
	{
		GameLoop loop = GameLoopLifecycleTests.CreateLoop(
			out FakeMonotonicClock clock);

		// 33,333,333 ns is 1.99999998 steps, leaving 0.99999998 of
		// the next step. Converting that to float must not report 1.
		clock.AdvanceNanoseconds(33_333_333);
		FrameTiming frame = loop.Advance();

		Assert.Equal(1, frame.FixedSteps);
		Assert.True(frame.InterpolationAlpha > 0.999f);
		Assert.True(frame.InterpolationAlpha < 1.0f);
		Assert.Equal(frame.InterpolationAlpha, loop.InterpolationAlpha);
	}

	[Fact]
	public void InterpolationAlpha_StaysInTheUnitInterval()
	{
		GameLoop loop = GameLoopLifecycleTests.CreateLoop(
			out FakeMonotonicClock clock);

		Random random = new(20261021);

		for (int index = 0; index < 10_000; index++)
		{
			clock.AdvanceNanoseconds(random.NextInt64(0, 40_000_000));
			FrameTiming frame = loop.Advance();

			Assert.InRange(frame.InterpolationAlpha, 0.0f, 0.9999999f);
			Assert.Equal(
				frame.InterpolationAlpha,
				loop.InterpolationAlpha);
		}
	}

	[Fact]
	public void FixedDelta_IsIndependentOfRenderRate()
	{
		GameLoop slow = GameLoopLifecycleTests.CreateLoop(
			out FakeMonotonicClock slowClock,
			maxFixedStepsPerFrame: 100);
		GameLoop fast = GameLoopLifecycleTests.CreateLoop(
			out FakeMonotonicClock fastClock,
			maxFixedStepsPerFrame: 100);

		List<float> slowDeltas = [];
		List<float> fastDeltas = [];

		slow.RegisterFixedUpdate(
			(in FixedUpdateContext context) =>
				slowDeltas.Add(context.FixedDeltaSeconds));
		fast.RegisterFixedUpdate(
			(in FixedUpdateContext context) =>
				fastDeltas.Add(context.FixedDeltaSeconds));

		for (int index = 0; index < 10; index++)
		{
			slowClock.AdvanceNanoseconds(100_000_000);
			slow.Advance();
		}

		for (int index = 0; index < 100; index++)
		{
			fastClock.AdvanceNanoseconds(10_000_000);
			fast.Advance();
		}

		Assert.Equal(60, slowDeltas.Count);
		Assert.Equal(60, fastDeltas.Count);
		Assert.All(
			slowDeltas,
			delta => Assert.Equal(1.0f / 60.0f, delta));
		Assert.All(
			fastDeltas,
			delta => Assert.Equal(1.0f / 60.0f, delta));
	}

	[Theory]
	[InlineData(1)]
	[InlineData(7)]
	[InlineData(30)]
	[InlineData(60)]
	[InlineData(120)]
	[InlineData(240)]
	public void SupportedFixedRates_KeepLongRunPhase(int rate)
	{
		GameLoop loop = GameLoopLifecycleTests.CreateLoop(
			out FakeMonotonicClock clock,
			fixedUpdatesPerSecond: rate,
			maxFixedStepsPerFrame: 100,
			maximumFrameDelta: TimeSpan.FromSeconds(1));

		// One simulated second in ten-millisecond application frames.
		for (int index = 0; index < 100; index++)
		{
			clock.AdvanceNanoseconds(10_000_000);
			loop.Advance();
		}

		Assert.Equal(rate, loop.TotalFixedSteps);
		Assert.Equal(1.0, loop.SimulationTimeSeconds, 9);
		Assert.Equal(0.0f, loop.InterpolationAlpha);
	}

	[Theory]
	[InlineData(0)]
	[InlineData(-1)]
	[InlineData(1001)]
	public void InvalidRates_AreRejected(int rate)
	{
		Assert.Throws<ArgumentOutOfRangeException>(
			() => new GameLoopSettings(fixedUpdatesPerSecond: rate));
	}
}
