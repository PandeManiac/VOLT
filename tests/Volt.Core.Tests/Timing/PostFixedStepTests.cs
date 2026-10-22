using Volt.Core.Timing;

namespace Volt.Core.Tests.Timing;

public sealed class PostFixedStepTests
{
	private const long FrameNanoseconds = 16_666_667;

	[Fact]
	public void PostFixed_RunsAfterFixedAndScheduledWork()
	{
		GameLoop loop = GameLoopLifecycleTests.CreateLoop(
			out FakeMonotonicClock clock);

		List<string> order = [];

		loop.RegisterFixedUpdate(
			(in FixedUpdateContext context) =>
				order.Add($"fixed {context.StepIndex}"));
		loop.Schedule.EveryHz(
			60,
			(in ScheduledUpdateContext context) =>
				order.Add($"scheduled {context.StepIndex}"));
		loop.RegisterPostFixedUpdate(
			(in PostFixedUpdateContext context) =>
				order.Add($"post {context.StepIndex}"));

		clock.AdvanceNanoseconds(2 * FrameNanoseconds);
		loop.Advance();

		Assert.Equal(
			[
				"fixed 1",
				"scheduled 1",
				"post 1",
				"fixed 2",
				"scheduled 2",
				"post 2"
			],
			order);
	}

	[Fact]
	public void PostFixed_ContextReportsTheCompletedStep()
	{
		GameLoop loop = GameLoopLifecycleTests.CreateLoop(
			out FakeMonotonicClock clock);

		PostFixedUpdateContext captured = default;

		loop.RegisterPostFixedUpdate(
			(in PostFixedUpdateContext context) => captured = context);

		clock.AdvanceNanoseconds(3 * FrameNanoseconds);
		loop.Advance();

		Assert.Equal(3, captured.StepIndex);
		Assert.Equal(3 / 60.0, captured.SimulationTimeSeconds, 9);
		Assert.False(captured.IsPaused);
	}

	[Fact]
	public void PostFixed_RunsOnceForASingleStep()
	{
		GameLoop loop = GameLoopLifecycleTests.CreateLoop(
			out FakeMonotonicClock clock);

		int posts = 0;
		bool paused = false;

		loop.RegisterPostFixedUpdate(
			(in PostFixedUpdateContext context) =>
			{
				posts++;
				paused = context.IsPaused;
			});

		loop.Pause();
		Assert.True(loop.StepOnce());

		FrameTiming frame = loop.Advance();

		Assert.Equal(1, frame.FixedSteps);
		Assert.Equal(1, posts);
		Assert.True(paused);
	}

	[Fact]
	public void PostFixed_SkipsStepsCancelledByPause()
	{
		GameLoop loop = GameLoopLifecycleTests.CreateLoop(
			out FakeMonotonicClock clock);

		int posts = 0;

		loop.RegisterFixedUpdate(
			(in FixedUpdateContext context) =>
			{
				if (context.StepIndex == 2)
				{
					loop.Pause();
				}
			});

		loop.RegisterPostFixedUpdate(
			(in PostFixedUpdateContext _) => posts++);

		clock.AdvanceNanoseconds(250_000_000);
		FrameTiming frame = loop.Advance();

		Assert.Equal(2, frame.FixedSteps);
		Assert.Equal(2, posts);
	}

	[Fact]
	public void PostFixed_StillRunsWhenAScheduledCallbackPauses()
	{
		GameLoop loop = GameLoopLifecycleTests.CreateLoop(
			out FakeMonotonicClock clock);

		long postStep = -1;

		loop.Schedule.EveryHz(
			60,
			(in ScheduledUpdateContext context) =>
			{
				if (context.StepIndex == 1)
				{
					loop.Pause();
				}
			});

		loop.RegisterPostFixedUpdate(
			(in PostFixedUpdateContext context) =>
				postStep = context.StepIndex);

		clock.AdvanceNanoseconds(250_000_000);
		FrameTiming frame = loop.Advance();

		// The pause ended the batch after the first step, but the
		// completed step still reported its committed state.
		Assert.Equal(1, frame.FixedSteps);
		Assert.Equal(1, postStep);
	}

	[Fact]
	public void PostFixed_RegistrationDuringAdvance_IsRejected()
	{
		GameLoop loop = GameLoopLifecycleTests.CreateLoop(
			out FakeMonotonicClock clock);

		Exception? failure = null;

		loop.RegisterFixedUpdate(
			(in FixedUpdateContext _) =>
			{
				failure = Record.Exception(
					() => loop.RegisterPostFixedUpdate(
						static (in PostFixedUpdateContext _) => { }));
			});

		clock.AdvanceNanoseconds(FrameNanoseconds);
		loop.Advance();

		Assert.IsType<InvalidOperationException>(failure);
	}

	[Fact]
	public void PostFixed_CanRegisterScheduledWorkForTheNextStep()
	{
		GameLoop loop = GameLoopLifecycleTests.CreateLoop(
			out FakeMonotonicClock clock);

		long registered = -1;
		long fired = -1;

		loop.RegisterPostFixedUpdate(
			(in PostFixedUpdateContext context) =>
			{
				if (registered >= 0)
				{
					return;
				}

				registered = context.StepIndex;

				loop.Schedule.After(
					TimeSpan.Zero,
					(in ScheduledUpdateContext scheduled) =>
						fired = scheduled.StepIndex);
			});

		clock.AdvanceNanoseconds(2 * FrameNanoseconds);
		loop.Advance();

		Assert.Equal(1, registered);
		Assert.Equal(2, fired);
	}

	[Fact]
	public void Unregister_RemovesOnlyTheMatchingCallback()
	{
		GameLoop loop = GameLoopLifecycleTests.CreateLoop(
			out FakeMonotonicClock clock);

		List<int> calls = [];

		PostFixedUpdateCallback first =
			(in PostFixedUpdateContext _) => calls.Add(1);
		PostFixedUpdateCallback second =
			(in PostFixedUpdateContext _) => calls.Add(2);

		loop.RegisterPostFixedUpdate(first);
		loop.RegisterPostFixedUpdate(second);

		Assert.True(loop.UnregisterPostFixedUpdate(first));
		Assert.False(loop.UnregisterPostFixedUpdate(first));

		clock.AdvanceNanoseconds(FrameNanoseconds);
		loop.Advance();

		Assert.Equal([2], calls);
	}

	[Fact]
	public void PostFixed_ReentrancyGuardStaysActive()
	{
		GameLoop loop = GameLoopLifecycleTests.CreateLoop(
			out FakeMonotonicClock clock);

		Exception? failure = null;

		loop.RegisterPostFixedUpdate(
			(in PostFixedUpdateContext _) =>
			{
				failure = Record.Exception(() => loop.Advance());
			});

		clock.AdvanceNanoseconds(FrameNanoseconds);
		FrameTiming frame = loop.Advance();

		Assert.IsType<InvalidOperationException>(failure);
		Assert.Equal(1, frame.FixedSteps);
		Assert.Equal(1, loop.LastFrame.FrameIndex);
		Assert.Equal(1, loop.TotalFixedSteps);

		clock.AdvanceNanoseconds(FrameNanoseconds);
		FrameTiming next = loop.Advance();

		Assert.Equal(1, next.FixedSteps);
		Assert.Equal(2, next.FrameIndex);
		Assert.Equal(2, loop.TotalFixedSteps);
	}

	[Fact]
	public void PostFixed_NullCallback_IsRejected()
	{
		GameLoop loop = GameLoopLifecycleTests.CreateLoop(out _);

		Assert.Throws<ArgumentNullException>(
			() => loop.RegisterPostFixedUpdate(null!));
	}
}
