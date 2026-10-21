using Volt.Core.Timing;

namespace Volt.Core.Tests.Timing;

public sealed class GameLoopLifecycleTests
{
	private const long FrameNanoseconds = 16_666_667;

	[Fact]
	public void Update_RunsOncePerFrame()
	{
		GameLoop loop = CreateLoop(out FakeMonotonicClock clock);
		int updates = 0;

		loop.RegisterUpdate((in UpdateContext _) => updates++);

		Advance(loop, clock, 10);

		Assert.Equal(10, updates);
		Assert.Equal(10, loop.LastFrame.FrameIndex);
	}

	[Fact]
	public void FixedUpdate_RunsZeroTimesWhenNoStepIsDue()
	{
		GameLoop loop = CreateLoop(out FakeMonotonicClock clock);
		int fixedUpdates = 0;

		loop.RegisterFixedUpdate(
			(in FixedUpdateContext _) => fixedUpdates++);

		clock.AdvanceNanoseconds(10_000_000);
		loop.Advance();

		Assert.Equal(0, fixedUpdates);
		Assert.Equal(0, loop.LastFrame.FixedSteps);
		Assert.Equal(0, loop.LastFrame.TotalFixedSteps);
	}

	[Fact]
	public void FixedUpdate_RunsEveryDueStepBeforeUpdate()
	{
		GameLoop loop = CreateLoop(out FakeMonotonicClock clock);
		List<string> order = [];

		loop.RegisterFixedUpdate(
			(in FixedUpdateContext context) =>
				order.Add($"fixed {context.StepIndex}"));
		loop.RegisterUpdate((in UpdateContext _) => order.Add("update"));

		clock.AdvanceNanoseconds(50_000_000);

		loop.Advance();

		Assert.Equal(
			[
				"fixed 1",
				"fixed 2",
				"fixed 3",
				"update"
			],
			order);
		Assert.Equal(3, loop.LastFrame.FixedSteps);
	}

	[Fact]
	public void Update_RunsOnceEvenWhenSeveralFixedStepsRun()
	{
		GameLoop loop = CreateLoop(out FakeMonotonicClock clock);
		int updates = 0;
		int fixedUpdates = 0;

		loop.RegisterUpdate((in UpdateContext _) => updates++);
		loop.RegisterFixedUpdate(
			(in FixedUpdateContext _) => fixedUpdates++);

		clock.AdvanceNanoseconds(250_000_000);
		loop.Advance();

		Assert.Equal(5, fixedUpdates);
		Assert.Equal(1, updates);
		Assert.Equal(5, loop.LastFrame.FixedSteps);
	}

	[Fact]
	public void Callbacks_RunInRegistrationOrder()
	{
		GameLoop loop = CreateLoop(out FakeMonotonicClock clock);
		List<int> updates = [];
		List<int> fixedUpdates = [];

		for (int index = 0; index < 4; index++)
		{
			int captured = index;

			loop.RegisterUpdate(
				(in UpdateContext _) => updates.Add(captured));
			loop.RegisterFixedUpdate(
				(in FixedUpdateContext _) => fixedUpdates.Add(captured));
		}

		Advance(loop, clock, 2);

		// Fixed updates run in registration order on every step.
		Assert.Equal([0, 1, 2, 3, 0, 1, 2, 3], fixedUpdates);

		// Updates run in registration order once per frame.
		Assert.Equal([0, 1, 2, 3, 0, 1, 2, 3], updates);
	}

	[Fact]
	public void UnregisterUpdate_StopsExecutionAndPreservesOrder()
	{
		GameLoop loop = CreateLoop(out FakeMonotonicClock clock);
		List<string> calls = [];

		UpdateCallback first = (in UpdateContext _) => calls.Add("first");
		UpdateCallback second = (in UpdateContext _) => calls.Add("second");
		UpdateCallback third = (in UpdateContext _) => calls.Add("third");

		loop.RegisterUpdate(first);
		loop.RegisterUpdate(second);
		loop.RegisterUpdate(third);

		loop.Advance();

		Assert.True(loop.UnregisterUpdate(second));
		Assert.False(loop.UnregisterUpdate(second));

		clock.AdvanceNanoseconds(FrameNanoseconds);
		loop.Advance();

		Assert.Equal(
			["first", "second", "third", "first", "third"],
			calls);
	}

	[Fact]
	public void UnregisterFixedUpdate_StopsExecution()
	{
		GameLoop loop = CreateLoop(out FakeMonotonicClock clock);
		int removed = 0;
		int kept = 0;

		FixedUpdateCallback removedCallback =
			(in FixedUpdateContext _) => removed++;
		FixedUpdateCallback keptCallback =
			(in FixedUpdateContext _) => kept++;

		loop.RegisterFixedUpdate(removedCallback);
		loop.RegisterFixedUpdate(keptCallback);

		Advance(loop, clock, 5);

		Assert.True(loop.UnregisterFixedUpdate(removedCallback));

		Advance(loop, clock, 5);

		Assert.Equal(5, removed);
		Assert.Equal(10, kept);
	}

	[Fact]
	public void RegisterUpdate_FromInsideAdvance_IsRejected()
	{
		GameLoop loop = CreateLoop(out FakeMonotonicClock clock);

		loop.RegisterUpdate((in UpdateContext _) =>
			loop.RegisterUpdate((in UpdateContext _) => { }));

		Assert.Throws<InvalidOperationException>(() => loop.Advance());
	}

	[Fact]
	public void RegisterFixedUpdate_FromInsideScheduledWork_IsRejected()
	{
		GameLoop loop = CreateLoop(out FakeMonotonicClock clock);

		loop.Schedule.After(
			TimeSpan.Zero,
			(in ScheduledUpdateContext _) =>
				loop.RegisterFixedUpdate((in FixedUpdateContext _) => { }));

		clock.AdvanceNanoseconds(FrameNanoseconds);

		Assert.Throws<InvalidOperationException>(() => loop.Advance());
	}

	[Fact]
	public void ThrowingCallback_DoesNotWedgeTheLoop()
	{
		GameLoop loop = CreateLoop(out FakeMonotonicClock clock);
		int calls = 0;

		loop.RegisterUpdate((in UpdateContext _) =>
		{
			calls++;

			if (calls == 1)
			{
				throw new InvalidOperationException("boom");
			}
		});

		Assert.Throws<InvalidOperationException>(() => loop.Advance());

		clock.AdvanceNanoseconds(FrameNanoseconds);

		Assert.Equal(2, loop.Advance().FrameIndex);
		Assert.Equal(2, calls);
	}

	[Fact]
	public void Update_ReceivesFrameDeltaAndPauseState()
	{
		GameLoop loop = CreateLoop(out FakeMonotonicClock clock);

		UpdateContext observed = default;

		loop.RegisterUpdate(
			(in UpdateContext context) => observed = context);

		clock.AdvanceNanoseconds(20_000_000);
		loop.Advance();

		Assert.Equal(1, observed.FrameIndex);
		Assert.Equal(0.02f, observed.DeltaSeconds, 6);
		Assert.Equal(TimeSpan.FromMilliseconds(20), observed.RealDelta);
		Assert.Equal(1, observed.FixedSteps);
		Assert.False(observed.IsPaused);
		Assert.InRange(observed.InterpolationAlpha, 0.0f, 0.999f);
	}

	[Fact]
	public void FixedUpdate_ReceivesStableDeltaAndExactTime()
	{
		GameLoop loop = CreateLoop(
			out FakeMonotonicClock clock,
			maxFixedStepsPerFrame: 100);
		List<FixedUpdateContext> steps = [];

		loop.RegisterFixedUpdate(
			(in FixedUpdateContext context) => steps.Add(context));

		clock.AdvanceNanoseconds(100_000_000);
		loop.Advance();

		Assert.Equal(6, steps.Count);

		for (int index = 0; index < steps.Count; index++)
		{
			FixedUpdateContext step = steps[index];

			Assert.Equal(index + 1, step.StepIndex);
			Assert.Equal(1.0f / 60.0f, step.FixedDeltaSeconds);
			Assert.Equal((index + 1) / 60.0, step.SimulationTimeSeconds, 9);
			Assert.False(step.IsPaused);
		}
	}

	[Fact]
	public void EmptyLoop_AllocatesNothingDuringConstructionCostsOnly()
	{
		// Registration is allowed to allocate; a loop with no callbacks
		// must still advance correctly.
		GameLoop loop = CreateLoop(out FakeMonotonicClock clock);

		Advance(loop, clock, 100);

		Assert.Equal(100, loop.LastFrame.FrameIndex);
		Assert.Equal(100, loop.TotalFixedSteps);
	}

	internal static GameLoop CreateLoop(
		out FakeMonotonicClock clock,
		int fixedUpdatesPerSecond = 60,
		int maxFixedStepsPerFrame = 5,
		TimeSpan? maximumFrameDelta = null)
	{
		clock = new FakeMonotonicClock();

		return new GameLoop(
			new GameLoopSettings(
				fixedUpdatesPerSecond,
				maxFixedStepsPerFrame,
				maximumFrameDelta ?? TimeSpan.FromMilliseconds(250)),
			clock);
	}

	internal static void Advance(
		GameLoop loop,
		FakeMonotonicClock clock,
		int frames)
	{
		for (int index = 0; index < frames; index++)
		{
			clock.AdvanceNanoseconds(FrameNanoseconds);
			loop.Advance();
		}
	}

	internal static void AdvanceSeconds(
		GameLoop loop,
		FakeMonotonicClock clock,
		int seconds)
	{
		for (int index = 0; index < seconds; index++)
		{
			clock.AdvanceNanoseconds(1_000_000_000);
			loop.Advance();
		}
	}
}
