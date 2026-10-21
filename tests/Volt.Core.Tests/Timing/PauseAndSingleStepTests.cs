using Volt.Core.Timing;

namespace Volt.Core.Tests.Timing;

public sealed class PauseAndSingleStepTests
{
	[Fact]
	public void Paused_UpdateRunsButSimulationDoesNot()
	{
		GameLoop loop = GameLoopLifecycleTests.CreateLoop(
			out FakeMonotonicClock clock);

		int updates = 0;
		int fixedUpdates = 0;

		loop.RegisterUpdate((in UpdateContext _) => updates++);
		loop.RegisterFixedUpdate(
			(in FixedUpdateContext _) => fixedUpdates++);

		loop.Pause();

		Assert.True(loop.IsPaused);

		for (int index = 0; index < 5; index++)
		{
			clock.AdvanceNanoseconds(1_000_000_000);
			FrameTiming frame = loop.Advance();

			Assert.True(frame.IsPaused);
			Assert.Equal(0, frame.FixedSteps);
			Assert.Equal(0.0f, frame.InterpolationAlpha);
		}

		Assert.Equal(5, updates);
		Assert.Equal(0, fixedUpdates);
		Assert.Equal(0, loop.TotalFixedSteps);
	}

	[Fact]
	public void Paused_DoesNotAccumulateTime()
	{
		GameLoop loop = GameLoopLifecycleTests.CreateLoop(
			out FakeMonotonicClock clock);

		// Leave a partial step behind before pausing.
		clock.AdvanceNanoseconds(10_000_000);
		loop.Advance();

		loop.Pause();

		clock.AdvanceNanoseconds(5_000_000_000);
		loop.Advance();

		loop.Resume();

		clock.AdvanceNanoseconds(16_666_667);
		loop.Advance();

		// Only the frame after resume contributes one step; the five
		// seconds spent paused are gone.
		Assert.Equal(1, loop.TotalFixedSteps);
	}

	[Fact]
	public void Resume_DiscardsTimeObservedWhilePaused()
	{
		GameLoop loop = GameLoopLifecycleTests.CreateLoop(
			out FakeMonotonicClock clock);

		loop.Pause();

		clock.AdvanceNanoseconds(10_000_000_000);
		loop.Resume();

		clock.AdvanceNanoseconds(16_666_667);
		FrameTiming frame = loop.Advance();

		Assert.Equal(1, frame.FixedSteps);
		Assert.Equal(TimeSpan.Zero, frame.DroppedTime);
	}

	[Fact]
	public void StepOnce_ExecutesExactlyOneStep()
	{
		GameLoop loop = GameLoopLifecycleTests.CreateLoop(
			out FakeMonotonicClock clock);

		List<FixedUpdateContext> steps = [];

		loop.RegisterFixedUpdate(
			(in FixedUpdateContext context) => steps.Add(context));

		Advance(loop, clock, 3);

		loop.Pause();

		Assert.False(loop.IsSingleStepPending);
		Assert.True(loop.StepOnce());
		Assert.True(loop.IsSingleStepPending);

		clock.AdvanceNanoseconds(5_000_000_000);
		FrameTiming frame = loop.Advance();

		Assert.Equal(1, frame.FixedSteps);
		Assert.True(frame.IsPaused);

		Assert.False(loop.IsSingleStepPending);
		Assert.Equal(4, loop.TotalFixedSteps);
		Assert.Equal(4, steps[^1].StepIndex);
		Assert.True(steps[^1].IsPaused);
		Assert.Equal(4.0 / 60.0, loop.SimulationTimeSeconds, 9);

		// The next paused frame executes nothing.
		clock.AdvanceNanoseconds(1_000_000_000);
		Assert.Equal(0, loop.Advance().FixedSteps);
	}

	[Fact]
	public void StepOnce_MultipleRequests_QueueOnlyOneStep()
	{
		GameLoop loop = GameLoopLifecycleTests.CreateLoop(
			out FakeMonotonicClock clock);

		loop.Pause();

		Assert.True(loop.StepOnce());
		Assert.False(loop.StepOnce());

		clock.AdvanceNanoseconds(1_000_000_000);
		Assert.Equal(1, loop.Advance().FixedSteps);

		clock.AdvanceNanoseconds(1_000_000_000);
		Assert.Equal(0, loop.Advance().FixedSteps);
	}

	[Fact]
	public void StepOnce_WhileRunning_IsIgnored()
	{
		GameLoop loop = GameLoopLifecycleTests.CreateLoop(
			out FakeMonotonicClock clock);

		Assert.False(loop.StepOnce());

		clock.AdvanceNanoseconds(16_666_667);
		FrameTiming frame = loop.Advance();

		Assert.Equal(1, frame.FixedSteps);
	}

	[Fact]
	public void StepOnce_RunsScheduledWorkDueOnThatStep()
	{
		GameLoop loop = GameLoopLifecycleTests.CreateLoop(
			out FakeMonotonicClock clock);

		List<long> invocations = [];

		loop.Schedule.EveryHz(
			1,
			(in ScheduledUpdateContext context) =>
				invocations.Add(context.StepIndex));

		Advance(loop, clock, 59);

		Assert.Empty(invocations);

		loop.Pause();
		loop.StepOnce();

		clock.AdvanceNanoseconds(1_000_000_000);
		loop.Advance();

		Assert.Equal([60], invocations);
		Assert.Equal(60, loop.TotalFixedSteps);
	}

	[Fact]
	public void Pause_ClearsAPendingSingleStep()
	{
		GameLoop loop = GameLoopLifecycleTests.CreateLoop(
			out FakeMonotonicClock clock);

		loop.Pause();
		Assert.True(loop.StepOnce());

		loop.Pause();

		Assert.False(loop.IsSingleStepPending);

		clock.AdvanceNanoseconds(1_000_000_000);
		Assert.Equal(0, loop.Advance().FixedSteps);
	}

	[Fact]
	public void RepeatedPauseResume_DoesNotBurst()
	{
		GameLoop loop = GameLoopLifecycleTests.CreateLoop(
			out FakeMonotonicClock clock);

		for (int index = 0; index < 100; index++)
		{
			loop.Pause();
			clock.AdvanceNanoseconds(1_000_000_000);
			loop.Resume();

			clock.AdvanceNanoseconds(16_666_667);
			FrameTiming frame = loop.Advance();

			Assert.Equal(1, frame.FixedSteps);
			Assert.Equal(TimeSpan.Zero, frame.DroppedTime);
		}

		Assert.Equal(100, loop.TotalFixedSteps);
		Assert.Equal(0, loop.TotalDroppedTime.Ticks);
	}

	[Fact]
	public void Pause_DiscardsThePartialStep()
	{
		GameLoop loop = GameLoopLifecycleTests.CreateLoop(
			out FakeMonotonicClock clock);

		clock.AdvanceNanoseconds(10_000_000);
		loop.Advance();

		loop.Pause();
		loop.Resume();

		clock.AdvanceNanoseconds(10_000_000);
		Assert.Equal(0, loop.Advance().FixedSteps);

		clock.AdvanceNanoseconds(10_000_000);
		Assert.Equal(1, loop.Advance().FixedSteps);
	}

	[Fact]
	public void ResetTiming_DiscardsAccumulatedTime()
	{
		GameLoop loop = GameLoopLifecycleTests.CreateLoop(
			out FakeMonotonicClock clock);

		clock.AdvanceNanoseconds(10_000_000);
		loop.Advance();

		loop.ResetTiming();

		clock.AdvanceNanoseconds(10_000_000);
		Assert.Equal(0, loop.Advance().FixedSteps);
	}

	private static void Advance(
		GameLoop loop,
		FakeMonotonicClock clock,
		int frames)
	{
		GameLoopLifecycleTests.Advance(loop, clock, frames);
	}
}
