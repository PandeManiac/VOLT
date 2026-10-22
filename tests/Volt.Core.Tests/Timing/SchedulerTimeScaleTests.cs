using Volt.Core.Timing;

namespace Volt.Core.Tests.Timing;

public sealed class SchedulerTimeScaleTests
{
	private const long FrameNanoseconds = 16_666_667;

	[Theory]
	[InlineData(0.5f, 10)]
	[InlineData(1.0f, 20)]
	[InlineData(2.0f, 40)]
	public void TenHertz_FollowsSimulationTime(float scale, int expected)
	{
		GameLoop loop = GameLoopLifecycleTests.CreateLoop(
			out FakeMonotonicClock clock,
			maxFixedStepsPerFrame: 100);

		loop.TimeScale = scale;

		int invocations = 0;

		loop.Schedule.EveryHz(
			10,
			(in ScheduledUpdateContext _) => invocations++);

		// Two real seconds of 60 Hz frames. Simulation time is
		// scaled, so the callback count scales with it too.
		for (int index = 0; index < 120; index++)
		{
			clock.AdvanceNanoseconds(FrameNanoseconds);
			loop.Advance();
		}

		Assert.Equal(expected, invocations);
		Assert.Equal(expected, loop.Schedule.TotalInvocations);
	}

	[Fact]
	public void FiveSecondDelay_AtHalfSpeed_TakesTenRealSeconds()
	{
		GameLoop loop = GameLoopLifecycleTests.CreateLoop(
			out FakeMonotonicClock clock);

		loop.TimeScale = 0.5f;

		long firedAtStep = -1;

		ScheduledHandle handle = loop.Schedule.After(
			TimeSpan.FromSeconds(5),
			(in ScheduledUpdateContext context) =>
				firedAtStep = context.StepIndex);

		Assert.Equal(300, loop.Schedule.NextDeadlineStep);

		// Six real seconds are three simulation seconds: not yet.
		for (int index = 0; index < 360; index++)
		{
			clock.AdvanceNanoseconds(FrameNanoseconds);
			loop.Advance();
		}

		Assert.Equal(-1, firedAtStep);
		Assert.Equal(180, loop.TotalFixedSteps);
		Assert.Equal(300, loop.Schedule.NextDeadlineStep);

		// Four more real seconds reach five simulation seconds.
		for (int index = 0; index < 240; index++)
		{
			clock.AdvanceNanoseconds(FrameNanoseconds);
			loop.Advance();
		}

		Assert.Equal(300, firedAtStep);
		Assert.Equal(300, loop.TotalFixedSteps);
		Assert.Equal(0, loop.Schedule.ActiveCount);

		Assert.False(loop.Schedule.Cancel(handle));
	}

	[Fact]
	public void MultipleScaleChangesWhilePending_DoNotMoveTheDeadline()
	{
		GameLoop loop = GameLoopLifecycleTests.CreateLoop(
			out FakeMonotonicClock clock);

		long firedAtStep = -1;

		loop.Schedule.After(
			TimeSpan.FromSeconds(3),
			(in ScheduledUpdateContext context) =>
				firedAtStep = context.StepIndex);

		Assert.Equal(180, loop.Schedule.NextDeadlineStep);

		loop.TimeScale = 0.5f;

		for (int index = 0; index < 60; index++)
		{
			clock.AdvanceNanoseconds(FrameNanoseconds);
			loop.Advance();
		}

		loop.TimeScale = 2.0f;

		for (int index = 0; index < 60; index++)
		{
			clock.AdvanceNanoseconds(FrameNanoseconds);
			loop.Advance();
		}

		loop.TimeScale = 1.0f;

		while (firedAtStep < 0)
		{
			clock.AdvanceNanoseconds(FrameNanoseconds);
			loop.Advance();
		}

		Assert.Equal(180, firedAtStep);
		Assert.Equal(180, loop.TotalFixedSteps);
	}

	[Fact]
	public void RegistrationDuringScaledStep_StillWaitsForTheNextStep()
	{
		GameLoop loop = GameLoopLifecycleTests.CreateLoop(
			out FakeMonotonicClock clock);

		loop.TimeScale = 2.0f;

		long registeredAt = -1;
		long firedAt = -1;

		loop.RegisterFixedUpdate(
			(in FixedUpdateContext context) =>
			{
				if (registeredAt >= 0)
				{
					return;
				}

				registeredAt = context.StepIndex;

				loop.Schedule.After(
					TimeSpan.Zero,
					(in ScheduledUpdateContext scheduled) =>
						firedAt = scheduled.StepIndex);
			});

		clock.AdvanceNanoseconds(50_000_000);
		FrameTiming frame = loop.Advance();

		Assert.Equal(1, registeredAt);
		Assert.Equal(2, firedAt);
		Assert.True(frame.FixedSteps >= 2);
	}

	[Fact]
	public void ScaleChangeDoesNotResetSchedulerOrdering()
	{
		GameLoop loop = GameLoopLifecycleTests.CreateLoop(
			out FakeMonotonicClock clock);

		List<int> order = [];

		loop.Schedule.After(
			TimeSpan.FromSeconds(1),
			(in ScheduledUpdateContext _) => order.Add(1));
		loop.Schedule.After(
			TimeSpan.FromSeconds(1),
			(in ScheduledUpdateContext _) => order.Add(2));

		loop.TimeScale = 0.5f;

		for (int index = 0; index < 240; index++)
		{
			clock.AdvanceNanoseconds(FrameNanoseconds);
			loop.Advance();
		}

		Assert.Equal([1, 2], order);
		Assert.Equal(120, loop.TotalFixedSteps);
	}
}
