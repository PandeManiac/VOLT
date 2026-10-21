using Volt.Core.Timing;

namespace Volt.Core.Tests.Timing;

/// <summary>
/// Covers the rational conversion from clock ticks to nanoseconds,
/// including the sub-nanosecond remainder carried between frames.
/// </summary>
public sealed class ClockConversionTests
{
	[Fact]
	public void ThreeTicksOfAThreeTickClock_ProduceExactlyOneSecond()
	{
		FakeMonotonicClock clock = new(ticksPerSecond: 3);
		GameLoop loop = new(
			new GameLoopSettings(
				fixedUpdatesPerSecond: 60,
				maxFixedStepsPerFrame: 100,
				maximumFrameDelta: TimeSpan.FromMilliseconds(500)),
			clock);

		int[] expectedSteps = [19, 20, 21];

		for (int tick = 0; tick < 3; tick++)
		{
			clock.AdvanceTicks(1);

			FrameTiming frame = loop.Advance();

			Assert.Equal(expectedSteps[tick], frame.FixedSteps);
			Assert.Equal(TimeSpan.Zero, frame.DroppedTime);
		}

		// Each tick is 333,333,333.33... ns. Truncating every tick
		// independently would deliver 999,999,999 ns and run only
		// 59 steps.
		Assert.Equal(60, loop.TotalFixedSteps);
		Assert.Equal(0.0f, loop.InterpolationAlpha);
		Assert.Equal(TimeSpan.Zero, loop.TotalDroppedTime);
	}

	[Theory]
	[InlineData(1)]
	[InlineData(3)]
	[InlineData(7)]
	[InlineData(1_000)]
	[InlineData(3_579_545)]
	[InlineData(10_000_000)]
	[InlineData(1_000_000_000)]
	public void OneSecondOfClockTicks_IsExactlySixtyFixedSteps(
		long ticksPerSecond)
	{
		FakeMonotonicClock clock = new(ticksPerSecond);
		GameLoop loop = new(
			new GameLoopSettings(
				fixedUpdatesPerSecond: 60,
				maxFixedStepsPerFrame: 100,
				maximumFrameDelta: TimeSpan.FromSeconds(10)),
			clock);

		const int chunks = 8;
		long ticksPerChunk = ticksPerSecond / chunks;
		long extraTicks = ticksPerSecond % chunks;

		for (int chunk = 0; chunk < chunks; chunk++)
		{
			clock.AdvanceTicks(
				ticksPerChunk + (chunk < extraTicks ? 1 : 0));

			loop.Advance();
		}

		Assert.Equal(60, loop.TotalFixedSteps);
		Assert.Equal(0.0f, loop.InterpolationAlpha);
		Assert.Equal(TimeSpan.Zero, loop.TotalDroppedTime);
	}

	[Fact]
	public void ManyShortIntervals_MatchOneLongInterval()
	{
		FakeMonotonicClock manyClock = new(ticksPerSecond: 3);
		GameLoop many = new(
			new GameLoopSettings(60, 100, TimeSpan.FromSeconds(10)),
			manyClock);

		for (int tick = 0; tick < 3; tick++)
		{
			manyClock.AdvanceTicks(1);
			many.Advance();
		}

		FakeMonotonicClock oneClock = new(ticksPerSecond: 3);
		GameLoop one = new(
			new GameLoopSettings(60, 100, TimeSpan.FromSeconds(10)),
			oneClock);

		oneClock.AdvanceTicks(3);
		one.Advance();

		Assert.Equal(60, many.TotalFixedSteps);
		Assert.Equal(many.TotalFixedSteps, one.TotalFixedSteps);
		Assert.Equal(0.0f, many.InterpolationAlpha);
		Assert.Equal(0.0f, one.InterpolationAlpha);
		Assert.Equal(TimeSpan.Zero, many.TotalDroppedTime);
		Assert.Equal(TimeSpan.Zero, one.TotalDroppedTime);
	}

	[Fact]
	public void ResetTiming_DiscardsThePartialStepWithoutLosingTheClockFraction()
	{
		FakeMonotonicClock clock = new(ticksPerSecond: 3);
		GameLoop loop = new(
			new GameLoopSettings(
				fixedUpdatesPerSecond: 60,
				maxFixedStepsPerFrame: 100,
				maximumFrameDelta: TimeSpan.FromMilliseconds(500)),
			clock);

		for (int tick = 0; tick < 3; tick++)
		{
			clock.AdvanceTicks(1);
			loop.Advance();
			loop.ResetTiming();
		}

		// ResetTiming intentionally discards the partial fixed step.
		// A fresh nanosecond conversion every frame would deliver
		// 19 + 19 + 19 steps; carrying the clock fraction keeps the
		// third tick's extra nanosecond and delivers 19 + 19 + 20.
		Assert.Equal(58, loop.TotalFixedSteps);
		Assert.InRange(loop.InterpolationAlpha, 0.0f, 0.000001f);
	}

	[Fact]
	public void PauseAndResume_DoNotLoseTheClockFraction()
	{
		FakeMonotonicClock clock = new(ticksPerSecond: 3);
		GameLoop loop = new(
			new GameLoopSettings(
				fixedUpdatesPerSecond: 60,
				maxFixedStepsPerFrame: 100,
				maximumFrameDelta: TimeSpan.FromMilliseconds(500)),
			clock);

		clock.AdvanceTicks(1);
		Assert.Equal(19, loop.Advance().FixedSteps);

		loop.Pause();
		clock.AdvanceTicks(3);
		loop.Resume();

		for (int tick = 0; tick < 3; tick++)
		{
			clock.AdvanceTicks(1);
			loop.Advance();
		}

		Assert.Equal(79, loop.TotalFixedSteps);
		Assert.Equal(0.0f, loop.InterpolationAlpha);
		Assert.Equal(TimeSpan.Zero, loop.TotalDroppedTime);
	}

	[Fact]
	public void BackwardClockMovement_IsTreatedAsZeroAndKeepsTheFraction()
	{
		FakeMonotonicClock clock = new(ticksPerSecond: 3);
		GameLoop loop = new(
			new GameLoopSettings(
				fixedUpdatesPerSecond: 60,
				maxFixedStepsPerFrame: 100,
				maximumFrameDelta: TimeSpan.FromSeconds(10)),
			clock);

		clock.AdvanceTicks(1);
		Assert.Equal(19, loop.Advance().FixedSteps);

		clock.AdvanceTicks(-2);
		FrameTiming rewind = loop.Advance();

		Assert.Equal(0, rewind.FixedSteps);
		Assert.Equal(TimeSpan.Zero, rewind.RealDelta);
		Assert.Equal(19, loop.TotalFixedSteps);

		clock.AdvanceTicks(3);
		FrameTiming forward = loop.Advance();

		Assert.Equal(60, forward.FixedSteps);
		Assert.Equal(79, loop.TotalFixedSteps);

		// The ignored rewind leaves the phase just below one step,
		// but the reported alpha still belongs to [0, 1).
		Assert.True(forward.InterpolationAlpha > 0.999f);
		Assert.True(forward.InterpolationAlpha < 1.0f);
	}

	[Fact]
	public void VeryLargeElapsedInterval_IsClampedAndTheLoopKeepsRunning()
	{
		FakeMonotonicClock clock = new(ticksPerSecond: 3);
		GameLoop loop = new(
			new GameLoopSettings(
				fixedUpdatesPerSecond: 60,
				maxFixedStepsPerFrame: 100,
				maximumFrameDelta: TimeSpan.FromMilliseconds(500)),
			clock);

		clock.AdvanceTicks(long.MaxValue / 2);
		FrameTiming frame = loop.Advance();

		Assert.Equal(30, frame.FixedSteps);
		Assert.Equal(TimeSpan.FromMilliseconds(500), frame.RealDelta);
		Assert.True(frame.DroppedTime.TotalDays > 1000);
		Assert.True(loop.TotalDroppedTime.TotalDays > 1000);

		clock.AdvanceTicks(1);
		FrameTiming next = loop.Advance();

		Assert.Equal(19, next.FixedSteps);
		Assert.Equal(TimeSpan.Zero, next.DroppedTime);
	}

	[Fact]
	public void EnormousTimestampDelta_SaturatesWithoutOverflow()
	{
		FakeMonotonicClock clock = new(ticksPerSecond: 1);
		GameLoop loop = new(
			new GameLoopSettings(
				fixedUpdatesPerSecond: 60,
				maxFixedStepsPerFrame: 100,
				maximumFrameDelta: TimeSpan.FromSeconds(10)),
			clock);

		clock.SetTimestamp(long.MaxValue / 4);
		clock.AdvanceTicks(long.MaxValue / 4);
		FrameTiming frame = loop.Advance();

		Assert.Equal(100, frame.FixedSteps);
		Assert.Equal(TimeSpan.FromSeconds(10), frame.RealDelta);
		Assert.True(frame.DroppedTime.TotalDays > 1000);
		Assert.True(loop.TotalDroppedTime.TotalDays > 1000);

		clock.AdvanceTicks(1);
		FrameTiming next = loop.Advance();

		Assert.Equal(60, next.FixedSteps);
		Assert.Equal(0.0f, next.InterpolationAlpha);
		Assert.Equal(TimeSpan.Zero, next.DroppedTime);
	}
}
