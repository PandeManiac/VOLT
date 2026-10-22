using Volt.Core.Timing;

namespace Volt.Core.Tests.Timing;

public sealed class TimeScaleLoopTests
{
	private const long FrameNanoseconds = 16_666_667;

	[Theory]
	[InlineData(0.25f, 240, 60)]
	[InlineData(0.5f, 120, 60)]
	[InlineData(1.0f, 60, 60)]
	[InlineData(1.5f, 120, 180)]
	[InlineData(2.0f, 60, 120)]
	[InlineData(4.0f, 60, 240)]
	public void Scale_ProducesTheDocumentedStepCount(
		float scale,
		int frames,
		int expectedSteps)
	{
		GameLoop loop = GameLoopLifecycleTests.CreateLoop(
			out FakeMonotonicClock clock);

		loop.TimeScale = scale;

		for (int index = 0; index < frames; index++)
		{
			clock.AdvanceNanoseconds(FrameNanoseconds);
			loop.Advance();
		}

		Assert.Equal(expectedSteps, loop.TotalFixedSteps);
		Assert.Equal(
			expectedSteps / 60.0,
			loop.SimulationTimeSeconds,
			9);
		Assert.InRange(loop.InterpolationAlpha, 0.0f, 0.9999999f);
	}

	[Fact]
	public void DefaultScale_IsNormalSpeed()
	{
		GameLoop loop = GameLoopLifecycleTests.CreateLoop(
			out FakeMonotonicClock clock);

		Assert.Equal(TimeScale.Normal, loop.TimeScale);

		for (int index = 0; index < 60; index++)
		{
			clock.AdvanceNanoseconds(FrameNanoseconds);
			loop.Advance();
		}

		FrameTiming frame = loop.LastFrame;

		Assert.Equal(TimeScale.Normal, frame.TimeScale);
		Assert.Equal(1, frame.FixedSteps);
		Assert.Equal(frame.RealDelta, frame.ScaledDelta);
		Assert.Equal(60, loop.TotalFixedSteps);
	}

	[Fact]
	public void HalfSpeed_KeepsTheFixedDeltaUnchanged()
	{
		GameLoop loop = GameLoopLifecycleTests.CreateLoop(
			out FakeMonotonicClock clock);

		loop.TimeScale = 0.5f;

		List<float> deltas = [];

		loop.RegisterFixedUpdate(
			(in FixedUpdateContext context) =>
				deltas.Add(context.FixedDeltaSeconds));

		for (int index = 0; index < 120; index++)
		{
			clock.AdvanceNanoseconds(FrameNanoseconds);
			loop.Advance();
		}

		Assert.Equal(60, deltas.Count);
		Assert.All(
			deltas,
			delta => Assert.Equal(1.0f / 60.0f, delta));
	}

	[Fact]
	public void UpdateDelta_StaysRealWhileScaledDeltaFollowsTheScale()
	{
		GameLoop loop = GameLoopLifecycleTests.CreateLoop(
			out FakeMonotonicClock clock);

		loop.TimeScale = 0.5f;

		UpdateContext captured = default;

		loop.RegisterUpdate(
			(in UpdateContext context) => captured = context);

		clock.AdvanceNanoseconds(100_000_000);
		loop.Advance();

		Assert.Equal(0.1f, captured.DeltaSeconds, 5);
		Assert.Equal(0.05f, captured.ScaledDeltaSeconds, 5);
		Assert.Equal(TimeSpan.FromMilliseconds(50), captured.ScaledDelta);
		Assert.Equal(TimeScale.FromSingle(0.5f), captured.TimeScale);
	}

	[Fact]
	public void ZeroScale_FreezesSimulationWithoutPausing()
	{
		GameLoop loop = GameLoopLifecycleTests.CreateLoop(
			out FakeMonotonicClock clock);

		int updates = 0;
		int fixedUpdates = 0;

		loop.RegisterUpdate((in UpdateContext _) => updates++);
		loop.RegisterFixedUpdate(
			(in FixedUpdateContext _) => fixedUpdates++);

		loop.TimeScale = 0.0f;

		for (int index = 0; index < 100; index++)
		{
			clock.AdvanceNanoseconds(FrameNanoseconds);
			FrameTiming frame = loop.Advance();

			Assert.Equal(0, frame.FixedSteps);
			Assert.Equal(TimeSpan.Zero, frame.ScaledDelta);
		}

		Assert.Equal(100, updates);
		Assert.Equal(0, fixedUpdates);
		Assert.Equal(0, loop.TotalFixedSteps);
		Assert.False(loop.IsPaused);
		Assert.Equal(TimeSpan.Zero, loop.TotalDroppedSimulationTime);
	}

	[Fact]
	public void ZeroScale_ThenNormalSpeed_DoesNotBurst()
	{
		GameLoop loop = GameLoopLifecycleTests.CreateLoop(
			out FakeMonotonicClock clock);

		loop.TimeScale = 0.0f;

		for (int index = 0; index < 120; index++)
		{
			clock.AdvanceNanoseconds(FrameNanoseconds);
			loop.Advance();
		}

		loop.TimeScale = 1.0f;

		clock.AdvanceNanoseconds(FrameNanoseconds);
		FrameTiming frame = loop.Advance();

		Assert.Equal(1, frame.FixedSteps);
		Assert.Equal(1, loop.TotalFixedSteps);
		Assert.Equal(TimeSpan.Zero, frame.DroppedTime);
	}

	[Fact]
	public void ZeroScale_StepOnceStillRunsExactlyOneStep()
	{
		GameLoop loop = GameLoopLifecycleTests.CreateLoop(
			out FakeMonotonicClock clock);

		loop.TimeScale = 0.0f;
		loop.Pause();

		Assert.True(loop.StepOnce());

		FrameTiming frame = loop.Advance();

		Assert.Equal(1, frame.FixedSteps);
		Assert.Equal(1, loop.TotalFixedSteps);
		Assert.Equal(1.0 / 60.0, loop.SimulationTimeSeconds, 9);
	}

	[Theory]
	[InlineData(0.5f)]
	[InlineData(2.0f)]
	public void StepOnce_IsIndependentOfScale(float scale)
	{
		GameLoop loop = GameLoopLifecycleTests.CreateLoop(
			out FakeMonotonicClock clock);

		float fixedDelta = 0.0f;
		long stepIndex = 0;

		loop.RegisterFixedUpdate(
			(in FixedUpdateContext context) =>
			{
				fixedDelta = context.FixedDeltaSeconds;
				stepIndex = context.StepIndex;
			});

		loop.TimeScale = scale;
		loop.Pause();

		Assert.True(loop.StepOnce());

		loop.Advance();

		Assert.Equal(1, loop.TotalFixedSteps);
		Assert.Equal(1, stepIndex);
		Assert.Equal(1.0f / 60.0f, fixedDelta);
	}

	[Fact]
	public void ScaleChangeBetweenFrames_AppliesToTheNextFrame()
	{
		GameLoop loop = GameLoopLifecycleTests.CreateLoop(
			out FakeMonotonicClock clock);

		for (int index = 0; index < 30; index++)
		{
			clock.AdvanceNanoseconds(FrameNanoseconds);
			loop.Advance();
		}

		Assert.Equal(30, loop.TotalFixedSteps);

		loop.TimeScale = 0.5f;

		for (int index = 0; index < 30; index++)
		{
			clock.AdvanceNanoseconds(FrameNanoseconds);
			loop.Advance();
		}

		Assert.Equal(45, loop.TotalFixedSteps);
		Assert.Equal(TimeScale.FromSingle(0.5f), loop.LastFrame.TimeScale);
	}

	[Fact]
	public void ScaleChangeInsideUpdate_AppliesToTheNextFrame()
	{
		GameLoop loop = GameLoopLifecycleTests.CreateLoop(
			out FakeMonotonicClock clock);

		bool changed = false;

		loop.RegisterUpdate(
			(in UpdateContext _) =>
			{
				if (!changed)
				{
					changed = true;
					loop.TimeScale = 0.0f;
				}
			});

		clock.AdvanceNanoseconds(50_000_000);
		FrameTiming first = loop.Advance();

		clock.AdvanceNanoseconds(50_000_000);
		FrameTiming second = loop.Advance();

		// The change was requested during the first frame, so that
		// frame still used normal speed; the next one did not.
		Assert.Equal(3, first.FixedSteps);
		Assert.Equal(0, second.FixedSteps);
		Assert.Equal(TimeScale.Normal, first.TimeScale);
		Assert.Equal(TimeScale.Zero, second.TimeScale);
	}

	[Fact]
	public void ScaleChangeInsideFixedUpdate_DoesNotAffectTheBatch()
	{
		GameLoop loop = GameLoopLifecycleTests.CreateLoop(
			out FakeMonotonicClock clock);

		loop.TimeScale = 2.0f;

		int steps = 0;
		bool changed = false;

		loop.RegisterFixedUpdate(
			(in FixedUpdateContext _) =>
			{
				steps++;

				if (!changed)
				{
					changed = true;
					loop.TimeScale = 0.0f;
				}
			});

		clock.AdvanceNanoseconds(250_000_000);
		FrameTiming frame = loop.Advance();

		Assert.Equal(5, frame.FixedSteps);
		Assert.Equal(5, steps);
		Assert.Equal(TimeScale.FromSingle(2.0f), frame.TimeScale);

		clock.AdvanceNanoseconds(FrameNanoseconds);
		FrameTiming frozen = loop.Advance();

		Assert.Equal(0, frozen.FixedSteps);
		Assert.Equal(TimeScale.Zero, frozen.TimeScale);
	}

	[Fact]
	public void ScaleChangeInsideScheduledCallback_DoesNotAffectTheBatch()
	{
		GameLoop loop = GameLoopLifecycleTests.CreateLoop(
			out FakeMonotonicClock clock);

		loop.TimeScale = 2.0f;

		int scheduledSteps = 0;

		loop.Schedule.EveryHz(
			60,
			(in ScheduledUpdateContext _) =>
			{
				scheduledSteps++;

				if (scheduledSteps == 1)
				{
					loop.TimeScale = 0.5f;
				}
			});

		clock.AdvanceNanoseconds(250_000_000);
		FrameTiming frame = loop.Advance();

		Assert.Equal(5, frame.FixedSteps);
		Assert.Equal(5, scheduledSteps);
		Assert.Equal(TimeScale.FromSingle(2.0f), frame.TimeScale);

		clock.AdvanceNanoseconds(FrameNanoseconds);
		FrameTiming next = loop.Advance();

		Assert.Equal(TimeScale.FromSingle(0.5f), next.TimeScale);
	}

	[Fact]
	public void PausePreservesTheScale_AndResumeUsesIt()
	{
		GameLoop loop = GameLoopLifecycleTests.CreateLoop(
			out FakeMonotonicClock clock);

		loop.TimeScale = 0.5f;
		loop.Pause();

		for (int index = 0; index < 30; index++)
		{
			clock.AdvanceNanoseconds(FrameNanoseconds);
			loop.Advance();
		}

		Assert.Equal(0, loop.TotalFixedSteps);
		Assert.Equal(TimeScale.FromSingle(0.5f), loop.TimeScale);

		loop.Resume();

		for (int index = 0; index < 30; index++)
		{
			clock.AdvanceNanoseconds(FrameNanoseconds);
			loop.Advance();
		}

		Assert.Equal(15, loop.TotalFixedSteps);
		Assert.Equal(TimeScale.FromSingle(0.5f), loop.LastFrame.TimeScale);
	}

	[Fact]
	public void DroppedTime_MeasuresBothClocks()
	{
		GameLoop loop = GameLoopLifecycleTests.CreateLoop(
			out FakeMonotonicClock clock);

		loop.TimeScale = 2.0f;

		clock.AdvanceNanoseconds(250_000_000);
		FrameTiming frame = loop.Advance();

		// 250 ms of real time is 500 ms of simulation time, which is
		// 30 steps. Five ran, so 25 steps were dropped: 416.67 ms of
		// simulation time and 208.33 ms of real time.
		Assert.Equal(5, frame.FixedSteps);
		Assert.InRange(
			frame.DroppedSimulationTime.TotalMilliseconds,
			416.6,
			416.7);
		Assert.InRange(frame.DroppedTime.TotalMilliseconds, 208.3, 208.4);
		Assert.Equal(TimeSpan.FromMilliseconds(500), frame.ScaledDelta);
		Assert.Equal(TimeSpan.FromMilliseconds(250), frame.RealDelta);
		Assert.Equal(loop.TotalDroppedTime, frame.DroppedTime);
		Assert.Equal(
			loop.TotalDroppedSimulationTime,
			frame.DroppedSimulationTime);
	}

	[Fact]
	public void MaximumFrameDelta_IsAppliedBeforeScaling()
	{
		GameLoop loop = GameLoopLifecycleTests.CreateLoop(
			out FakeMonotonicClock clock);

		loop.TimeScale = 4.0f;

		clock.AdvanceNanoseconds(500_000_000);
		FrameTiming frame = loop.Advance();

		Assert.Equal(TimeSpan.FromMilliseconds(250), frame.RealDelta);
		Assert.Equal(TimeSpan.FromSeconds(1), frame.ScaledDelta);
		Assert.Equal(5, frame.FixedSteps);
		Assert.True(
			frame.DroppedSimulationTime.TotalMilliseconds > 1900.0);
		Assert.True(frame.DroppedTime.TotalMilliseconds > 475.0);
	}

	[Fact]
	public void HighScale_RespectsTheFixedStepCap()
	{
		GameLoop loop = GameLoopLifecycleTests.CreateLoop(
			out FakeMonotonicClock clock);

		loop.TimeScale = 4.0f;

		for (int index = 0; index < 100; index++)
		{
			clock.AdvanceNanoseconds(10_000_000);
			FrameTiming frame = loop.Advance();

			Assert.InRange(frame.FixedSteps, 0, 5);
		}

		// Ten milliseconds at four times speed is 40 ms of
		// simulation time, or 2.4 steps; the cap never binds here.
		Assert.Equal(240, loop.TotalFixedSteps);
		Assert.Equal(TimeSpan.Zero, loop.TotalDroppedSimulationTime);
	}

	[Fact]
	public void ManyShortFrames_MatchOneLongRunAtFractionalScale()
	{
		const long wideFrameNanoseconds = 33_333_333;
		const int frames = 100;

		GameLoop shortFrames = GameLoopLifecycleTests.CreateLoop(
			out FakeMonotonicClock shortClock,
			maxFixedStepsPerFrame: 100,
			maximumFrameDelta: TimeSpan.FromSeconds(10));

		GameLoop longFrame = GameLoopLifecycleTests.CreateLoop(
			out FakeMonotonicClock longClock,
			maxFixedStepsPerFrame: 100,
			maximumFrameDelta: TimeSpan.FromSeconds(10));

		shortFrames.TimeScale = 0.3f;
		longFrame.TimeScale = 0.3f;

		for (int index = 0; index < frames; index++)
		{
			shortClock.AdvanceNanoseconds(wideFrameNanoseconds);
			shortFrames.Advance();
		}

		longClock.AdvanceNanoseconds(frames * wideFrameNanoseconds);
		longFrame.Advance();

		// 3.3333333 real seconds at 0.3x are just over one
		// simulation second, or 60 whole steps. Splitting the run
		// into frames must not change the result.
		Assert.Equal(60, shortFrames.TotalFixedSteps);
		Assert.Equal(60, longFrame.TotalFixedSteps);
		Assert.Equal(
			shortFrames.InterpolationAlpha,
			longFrame.InterpolationAlpha,
			5);
	}

	[Fact]
	public void LongRunAtHalfSpeed_DoesNotDrift()
	{
		GameLoop loop = GameLoopLifecycleTests.CreateLoop(
			out FakeMonotonicClock clock,
			maxFixedStepsPerFrame: 100);

		loop.TimeScale = 0.5f;

		for (int index = 0; index < 1_000; index++)
		{
			clock.AdvanceNanoseconds(1_000_000);
			loop.Advance();
		}

		// One thousand real milliseconds are half a simulation
		// second, which is exactly thirty steps.
		Assert.Equal(30, loop.TotalFixedSteps);
		Assert.Equal(0.5, loop.SimulationTimeSeconds, 9);
	}

	[Fact]
	public void MaximumScale_DoesNotOverflowTheAccumulator()
	{
		GameLoop loop = GameLoopLifecycleTests.CreateLoop(
			out FakeMonotonicClock clock,
			maxFixedStepsPerFrame: 100);

		loop.TimeScale = TimeScale.Maximum;

		clock.AdvanceNanoseconds(long.MaxValue / 2);
		FrameTiming frame = loop.Advance();

		// The real delta is clamped to 250 ms, which is one
		// simulation second at 4x and therefore 60 steps.
		Assert.Equal(60, frame.FixedSteps);
		Assert.True(frame.DroppedSimulationTime > TimeSpan.Zero);
		Assert.True(loop.TotalFixedSteps > 0);
	}

	[Fact]
	public void InvalidScaleChange_IsRejectedBeforeItReachesTheLoop()
	{
		GameLoop loop = GameLoopLifecycleTests.CreateLoop(
			out _);

		Assert.Throws<ArgumentOutOfRangeException>(
			() => loop.TimeScale = -0.5f);

		Assert.Throws<ArgumentOutOfRangeException>(
			() => loop.TimeScale = 5.0f);

		Assert.Equal(TimeScale.Normal, loop.TimeScale);
	}

	[Fact]
	public void DefaultScale_AssignmentIsRejected()
	{
		GameLoop loop = GameLoopLifecycleTests.CreateLoop(
			out FakeMonotonicClock clock);

		loop.TimeScale = 0.5f;

		Assert.Throws<ArgumentException>(
			() => loop.TimeScale = default);

		Assert.Equal(TimeScale.FromSingle(0.5f), loop.TimeScale);

		// Half speed needs two frame intervals to accumulate one
		// fixed step.
		clock.AdvanceNanoseconds(FrameNanoseconds * 2);
		FrameTiming frame = loop.Advance();

		Assert.True(frame.FixedSteps > 0);
		Assert.Equal(TimeScale.FromSingle(0.5f), frame.TimeScale);
		Assert.Equal(TimeScale.FromSingle(0.5f), loop.EffectiveTimeScale);
	}

	[Fact]
	public void ZeroScale_IsDistinctFromDefaultAndStillFreezes()
	{
		GameLoop loop = GameLoopLifecycleTests.CreateLoop(
			out FakeMonotonicClock clock);

		loop.TimeScale = TimeScale.Zero;

		Assert.Throws<ArgumentException>(
			() => loop.TimeScale = default);

		clock.AdvanceNanoseconds(FrameNanoseconds);
		FrameTiming frame = loop.Advance();

		Assert.Equal(0, frame.FixedSteps);
		Assert.Equal(TimeScale.Zero, frame.TimeScale);
		Assert.False(loop.IsPaused);
	}

	[Fact]
	public void DefaultScale_InsideCallbackIsRejectedAndTheLoopContinues()
	{
		GameLoop loop = GameLoopLifecycleTests.CreateLoop(
			out FakeMonotonicClock clock);

		FixedUpdateCallback callback =
			(in FixedUpdateContext _) => loop.TimeScale = default;

		loop.TimeScale = 0.5f;
		loop.RegisterFixedUpdate(callback);

		clock.AdvanceNanoseconds(FrameNanoseconds * 2);

		Assert.Throws<ArgumentException>(() => loop.Advance());
		Assert.Equal(TimeScale.FromSingle(0.5f), loop.TimeScale);

		loop.UnregisterFixedUpdate(callback);
		clock.AdvanceNanoseconds(FrameNanoseconds * 2);

		Assert.Null(Record.Exception(() => loop.Advance()));
		Assert.Equal(TimeScale.FromSingle(0.5f), loop.TimeScale);
	}

	[Fact]
	public void ScaleChangeInsideCallback_IsNotAReentrancyViolation()
	{
		GameLoop loop = GameLoopLifecycleTests.CreateLoop(
			out FakeMonotonicClock clock);

		loop.RegisterUpdate(
			(in UpdateContext _) => loop.TimeScale = 2.0f);
		loop.RegisterFixedUpdate(
			(in FixedUpdateContext _) => loop.TimeScale = 0.5f);

		clock.AdvanceNanoseconds(FrameNanoseconds);

		Assert.Null(Record.Exception(() => loop.Advance()));
	}
}
