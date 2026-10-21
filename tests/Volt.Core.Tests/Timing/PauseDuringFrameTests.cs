using Volt.Core.Timing;

namespace Volt.Core.Tests.Timing;

/// <summary>
/// Covers a pause requested from inside a callback. The executing step
/// completes, later steps of the same application frame do not run,
/// and the discarded debt cannot burst after resuming.
/// </summary>
public sealed class PauseDuringFrameTests
{
	[Fact]
	public void PauseInsideFixedUpdate_StopsTheRestOfTheFrame()
	{
		GameLoop loop = GameLoopLifecycleTests.CreateLoop(
			out FakeMonotonicClock clock,
			maxFixedStepsPerFrame: 10);
		List<long> steps = [];
		int updates = 0;
		bool paused = false;

		loop.RegisterFixedUpdate((in FixedUpdateContext context) =>
		{
			steps.Add(context.StepIndex);

			if (!paused)
			{
				paused = true;
				loop.Pause();
			}
		});
		loop.RegisterUpdate((in UpdateContext _) => updates++);

		// Six steps are due; the first one pauses the loop.
		clock.AdvanceNanoseconds(100_000_000);
		FrameTiming frame = loop.Advance();

		Assert.Equal(1, frame.FixedSteps);
		Assert.Equal([1L], steps);
		Assert.Equal(1, updates);
		Assert.True(loop.IsPaused);
		Assert.False(frame.IsPaused);
		Assert.Equal(1, frame.TotalFixedSteps);
		Assert.Equal(1, loop.TotalFixedSteps);
		Assert.InRange(frame.DroppedTime.TotalMilliseconds, 83.3, 83.4);
		Assert.Equal(loop.TotalDroppedTime, frame.DroppedTime);

		// The rest of the frame's debt is gone, not banked.
		clock.AdvanceNanoseconds(1_000_000_000);
		Assert.Equal(0, loop.Advance().FixedSteps);
		Assert.Equal(1, loop.TotalFixedSteps);

		// Resuming does not replay the discarded steps either.
		loop.Resume();
		clock.AdvanceNanoseconds(16_666_667);
		Assert.Equal(1, loop.Advance().FixedSteps);
	}

	[Fact]
	public void PauseInsideScheduledCallback_StopsTheRestOfTheFrame()
	{
		GameLoop loop = GameLoopLifecycleTests.CreateLoop(
			out FakeMonotonicClock clock,
			maxFixedStepsPerFrame: 10);
		List<long> invocations = [];
		int updates = 0;

		loop.Schedule.EveryHz(60, (in ScheduledUpdateContext context) =>
		{
			invocations.Add(context.StepIndex);

			if (context.StepIndex == 1)
			{
				loop.Pause();
			}
		});
		loop.RegisterUpdate((in UpdateContext _) => updates++);

		clock.AdvanceNanoseconds(100_000_000);
		FrameTiming frame = loop.Advance();

		Assert.Equal(1, frame.FixedSteps);
		Assert.Equal([1L], invocations);
		Assert.Equal(1, updates);
		Assert.True(loop.IsPaused);
		Assert.InRange(frame.DroppedTime.TotalMilliseconds, 83.3, 83.4);
	}

	[Fact]
	public void PauseOnTheFirstOfFiveDueSteps_DropsFourSteps()
	{
		GameLoop loop = GameLoopLifecycleTests.CreateLoop(
			out FakeMonotonicClock clock,
			maxFixedStepsPerFrame: 10);
		List<long> steps = [];
		bool paused = false;

		loop.RegisterFixedUpdate((in FixedUpdateContext context) =>
		{
			steps.Add(context.StepIndex);

			if (!paused)
			{
				paused = true;
				loop.Pause();
			}
		});

		clock.AdvanceNanoseconds(5 * 16_666_667);
		FrameTiming frame = loop.Advance();

		Assert.Equal(1, frame.FixedSteps);
		Assert.Equal([1L], steps);
		Assert.InRange(frame.DroppedTime.TotalMilliseconds, 66.6, 66.7);
	}

	[Fact]
	public void PauseOnTheFinalDueStep_RunsEveryStep()
	{
		GameLoop loop = GameLoopLifecycleTests.CreateLoop(
			out FakeMonotonicClock clock,
			maxFixedStepsPerFrame: 10);
		List<long> steps = [];
		int updates = 0;

		loop.RegisterFixedUpdate((in FixedUpdateContext context) =>
		{
			steps.Add(context.StepIndex);

			if (context.StepIndex == 5)
			{
				loop.Pause();
			}
		});
		loop.RegisterUpdate((in UpdateContext _) => updates++);

		clock.AdvanceNanoseconds(5 * 16_666_667);
		FrameTiming frame = loop.Advance();

		Assert.Equal(5, frame.FixedSteps);
		Assert.Equal([1L, 2L, 3L, 4L, 5L], steps);
		Assert.Equal(1, updates);
		Assert.Equal(TimeSpan.Zero, frame.DroppedTime);
		Assert.True(loop.IsPaused);
	}

	[Fact]
	public void PauseWhileAlreadyPaused_CompletesTheSingleStep()
	{
		GameLoop loop = GameLoopLifecycleTests.CreateLoop(
			out FakeMonotonicClock clock);
		List<long> steps = [];

		loop.RegisterFixedUpdate((in FixedUpdateContext context) =>
		{
			steps.Add(context.StepIndex);
			loop.Pause();
		});

		loop.Pause();
		Assert.True(loop.StepOnce());

		clock.AdvanceNanoseconds(1_000_000_000);
		FrameTiming frame = loop.Advance();

		Assert.Equal(1, frame.FixedSteps);
		Assert.Equal([1L], steps);
		Assert.True(frame.IsPaused);
		Assert.True(loop.IsPaused);
		Assert.False(loop.IsSingleStepPending);
	}

	[Fact]
	public void PauseImmediatelyBeforeStepOnce_RunsOneMoreStepNextFrame()
	{
		GameLoop loop = GameLoopLifecycleTests.CreateLoop(
			out FakeMonotonicClock clock,
			maxFixedStepsPerFrame: 10);
		List<long> steps = [];
		bool paused = false;

		loop.RegisterFixedUpdate((in FixedUpdateContext context) =>
		{
			steps.Add(context.StepIndex);

			if (paused)
			{
				return;
			}

			paused = true;
			loop.Pause();

			Assert.True(loop.StepOnce());
		});

		// Six steps are due, but the first pauses and requests one.
		clock.AdvanceNanoseconds(100_000_000);
		FrameTiming pausedFrame = loop.Advance();

		Assert.Equal(1, pausedFrame.FixedSteps);
		Assert.Equal([1L], steps);
		Assert.True(loop.IsSingleStepPending);

		FrameTiming singleStep = loop.Advance();

		Assert.Equal(1, singleStep.FixedSteps);
		Assert.True(singleStep.IsPaused);
		Assert.Equal([1L, 2L], steps);
		Assert.False(loop.IsSingleStepPending);
	}
}
