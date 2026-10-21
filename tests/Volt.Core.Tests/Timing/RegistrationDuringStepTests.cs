using Volt.Core.Timing;

namespace Volt.Core.Tests.Timing;

/// <summary>
/// Covers work registered from inside a fixed step: the scheduler must
/// treat the currently executing step as the registration time.
/// </summary>
public sealed class RegistrationDuringStepTests
{
	[Fact]
	public void FixedUpdate_AfterZero_RunsOnTheNextStep()
	{
		GameLoop loop = GameLoopLifecycleTests.CreateLoop(
			out FakeMonotonicClock clock);
		List<long> invocations = [];
		bool registered = false;

		loop.RegisterFixedUpdate((in FixedUpdateContext _) =>
		{
			if (registered)
			{
				return;
			}

			registered = true;

			loop.Schedule.After(
				TimeSpan.Zero,
				(in ScheduledUpdateContext context) =>
					invocations.Add(context.StepIndex));
		});

		Advance(loop, clock, 3);

		Assert.Equal([2L], invocations);
		Assert.Equal(3, loop.TotalFixedSteps);
	}

	[Fact]
	public void FixedUpdate_AfterOneSecond_RunsSixtyStepsLater()
	{
		GameLoop loop = GameLoopLifecycleTests.CreateLoop(
			out FakeMonotonicClock clock);
		List<long> invocations = [];
		bool registered = false;

		loop.RegisterFixedUpdate((in FixedUpdateContext _) =>
		{
			if (registered)
			{
				return;
			}

			registered = true;

			loop.Schedule.After(
				TimeSpan.FromSeconds(1),
				(in ScheduledUpdateContext context) =>
					invocations.Add(context.StepIndex));
		});

		// Registering during step 1 makes step 61 the deadline.
		Advance(loop, clock, 61);

		Assert.Equal([61L], invocations);
	}

	[Fact]
	public void FixedUpdate_EveryHz10_StartsSixStepsAfterRegistration()
	{
		GameLoop loop = GameLoopLifecycleTests.CreateLoop(
			out FakeMonotonicClock clock);
		List<long> invocations = [];
		bool registered = false;

		loop.RegisterFixedUpdate((in FixedUpdateContext _) =>
		{
			if (registered)
			{
				return;
			}

			registered = true;

			loop.Schedule.EveryHz(
				10,
				(in ScheduledUpdateContext context) =>
					invocations.Add(context.StepIndex));
		});

		Advance(loop, clock, 19);

		Assert.Equal([7L, 13L, 19L], invocations);
	}

	[Fact]
	public void FixedUpdate_EveryHz7_StartsItsRationalCadenceAtRegistration()
	{
		GameLoop loop = GameLoopLifecycleTests.CreateLoop(
			out FakeMonotonicClock clock);
		List<long> invocations = [];
		bool registered = false;

		loop.RegisterFixedUpdate((in FixedUpdateContext _) =>
		{
			if (registered)
			{
				return;
			}

			registered = true;

			loop.Schedule.EveryHz(
				7,
				(in ScheduledUpdateContext context) =>
					invocations.Add(context.StepIndex));
		});

		Advance(loop, clock, 30);

		// 60/7 steps per occurrence, measured from step 1, alternates
		// 9 and 8 step gaps instead of being truncated to 8.
		Assert.Equal([10L, 19L, 27L], invocations);
	}

	[Fact]
	public void NoCallbackRunsTwiceOnASingleStep()
	{
		GameLoop loop = GameLoopLifecycleTests.CreateLoop(
			out FakeMonotonicClock clock);
		List<long> ticks = [];
		List<long> delayed = [];

		loop.Schedule.EveryHz(60, (in ScheduledUpdateContext context) =>
		{
			ticks.Add(context.StepIndex);

			if (context.StepIndex == 1)
			{
				loop.Schedule.After(
					TimeSpan.Zero,
					(in ScheduledUpdateContext inner) =>
						delayed.Add(inner.StepIndex));
			}
		});

		clock.AdvanceNanoseconds(60_000_000);
		FrameTiming frame = loop.Advance();

		Assert.Equal(3, frame.FixedSteps);
		Assert.Equal([1L, 2L, 3L], ticks);
		Assert.Equal([2L], delayed);
		Assert.Equal(4, loop.Schedule.TotalInvocations);
	}

	private static void Advance(
		GameLoop loop,
		FakeMonotonicClock clock,
		int frames)
	{
		GameLoopLifecycleTests.Advance(loop, clock, frames);
	}
}
