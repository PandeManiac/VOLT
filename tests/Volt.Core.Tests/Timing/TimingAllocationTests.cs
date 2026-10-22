using Volt.Core.Timing;

namespace Volt.Core.Tests.Timing;

public sealed class TimingAllocationTests
{
	private const int WarmupFrames = 200;
	private const int MeasuredFrames = 10_000;

	[Fact]
	public void EmptyCallbacks_AllocateNothingPerFrame()
	{
		GameLoop loop = CreateLoop(out FakeMonotonicClock clock);

		loop.RegisterUpdate(static (in UpdateContext _) => { });
		loop.RegisterFixedUpdate(static (in FixedUpdateContext _) => { });

		AssertZeroAllocations(loop, clock, MeasuredFrames);
	}

	[Fact]
	public void IdleScheduleWithManyTasks_AllocatesNothingPerFrame()
	{
		GameLoop loop = CreateLoop(out FakeMonotonicClock clock);

		for (int index = 0; index < 32; index++)
		{
			loop.Schedule.Every(
				TimeSpan.FromSeconds(30),
				static (in ScheduledUpdateContext _) => { });
		}

		AssertZeroAllocations(loop, clock, MeasuredFrames);
	}

	[Fact]
	public void ScheduledCallbacksBecomingDue_AllocateNothing()
	{
		GameLoop loop = CreateLoop(out FakeMonotonicClock clock);

		loop.Schedule.EveryHz(
			60,
			static (in ScheduledUpdateContext _) => { });

		AssertZeroAllocations(loop, clock, MeasuredFrames);
	}

	[Theory]
	[InlineData(0.5f)]
	[InlineData(1.5f)]
	[InlineData(2.0f)]
	[InlineData(4.0f)]
	public void ScaledFrames_AllocateNothing(float scale)
	{
		GameLoop loop = CreateLoop(out FakeMonotonicClock clock);

		loop.TimeScale = scale;
		loop.RegisterUpdate(static (in UpdateContext _) => { });
		loop.RegisterFixedUpdate(static (in FixedUpdateContext _) => { });

		AssertZeroAllocations(loop, clock, MeasuredFrames);
	}

	[Fact]
	public void PostFixedCallbacks_AllocateNothing()
	{
		GameLoop loop = CreateLoop(out FakeMonotonicClock clock);

		loop.RegisterFixedUpdate(static (in FixedUpdateContext _) => { });
		loop.RegisterPostFixedUpdate(
			static (in PostFixedUpdateContext _) => { });

		AssertZeroAllocations(loop, clock, MeasuredFrames);
	}

	[Fact]
	public void AlternatingScaleChanges_AllocateNothing()
	{
		GameLoop loop = CreateLoop(out FakeMonotonicClock clock);

		for (int index = 0; index < WarmupFrames; index++)
		{
			loop.TimeScale = (index & 1) == 0 ? 0.5f : 2.0f;
			clock.AdvanceNanoseconds(16_666_667);
			loop.Advance();
		}

		long before = GC.GetAllocatedBytesForCurrentThread();

		for (int index = 0; index < MeasuredFrames; index++)
		{
			loop.TimeScale = (index & 1) == 0 ? 0.5f : 2.0f;
			clock.AdvanceNanoseconds(16_666_667);
			loop.Advance();
		}

		long after = GC.GetAllocatedBytesForCurrentThread();

		Assert.Equal(before, after);
	}

	[Fact]
	public void FrozenSimulation_AllocatesNothing()
	{
		GameLoop loop = CreateLoop(out FakeMonotonicClock clock);

		loop.TimeScale = 0.0f;
		loop.RegisterUpdate(static (in UpdateContext _) => { });

		AssertZeroAllocations(loop, clock, MeasuredFrames);
	}

	[Fact]
	public void MultipleRepeatingTasks_AllocateNothing()
	{
		GameLoop loop = CreateLoop(out FakeMonotonicClock clock);

		for (int index = 0; index < 8; index++)
		{
			loop.Schedule.EveryHz(
				10,
				static (in ScheduledUpdateContext _) => { });
		}

		AssertZeroAllocations(loop, clock, MeasuredFrames);
	}

	[Fact]
	public void PauseResume_AllocatesNothing()
	{
		GameLoop loop = CreateLoop(out FakeMonotonicClock clock);
		loop.RegisterUpdate(static (in UpdateContext _) => { });

		for (int index = 0; index < WarmupFrames; index++)
		{
			loop.Pause();
			clock.AdvanceNanoseconds(1_000_000);
			loop.Resume();
			clock.AdvanceNanoseconds(16_666_667);
			loop.Advance();
		}

		long before = GC.GetAllocatedBytesForCurrentThread();

		for (int index = 0; index < MeasuredFrames; index++)
		{
			loop.Pause();
			clock.AdvanceNanoseconds(1_000_000);
			loop.Resume();
			clock.AdvanceNanoseconds(16_666_667);
			loop.Advance();
		}

		long after = GC.GetAllocatedBytesForCurrentThread();

		Assert.Equal(before, after);
	}

	[Fact]
	public void SingleStep_AllocatesNothing()
	{
		GameLoop loop = CreateLoop(out FakeMonotonicClock clock);
		loop.RegisterUpdate(static (in UpdateContext _) => { });
		loop.RegisterFixedUpdate(static (in FixedUpdateContext _) => { });

		loop.Pause();

		for (int index = 0; index < WarmupFrames; index++)
		{
			loop.StepOnce();
			loop.Advance();
		}

		long before = GC.GetAllocatedBytesForCurrentThread();

		for (int index = 0; index < MeasuredFrames; index++)
		{
			loop.StepOnce();
			loop.Advance();
		}

		long after = GC.GetAllocatedBytesForCurrentThread();

		Assert.Equal(before, after);
	}

	[Fact]
	public void LateRegistrationFollowedByRepeatedExecution_AllocatesNothing()
	{
		GameLoop loop = CreateLoop(out FakeMonotonicClock clock);

		for (int index = 0; index < WarmupFrames; index++)
		{
			clock.AdvanceNanoseconds(16_666_667);
			loop.Advance();
		}

		// Registration is allowed to allocate. Everything after it,
		// including the deadlines derived from the registration step,
		// must not.
		loop.Schedule.After(
			TimeSpan.FromSeconds(1),
			static (in ScheduledUpdateContext _) => { });
		loop.Schedule.EveryHz(
			10,
			static (in ScheduledUpdateContext _) => { });

		long before = GC.GetAllocatedBytesForCurrentThread();

		for (int index = 0; index < MeasuredFrames; index++)
		{
			clock.AdvanceNanoseconds(16_666_667);
			loop.Advance();
		}

		long after = GC.GetAllocatedBytesForCurrentThread();

		Assert.Equal(before, after);
		Assert.True(loop.Schedule.TotalInvocations > 1_500);
	}

	[Fact]
	public void RegistrationMayAllocateButCancellationDoesNot()
	{
		GameLoop loop = CreateLoop(out FakeMonotonicClock clock);
		ScheduledHandle handle = loop.Schedule.EveryHz(
			10,
			static (in ScheduledUpdateContext _) => { });

		loop.Schedule.Cancel(handle);

		Assert.Equal(0, loop.Schedule.ActiveCount);
	}

	private static void AssertZeroAllocations(
		GameLoop loop,
		FakeMonotonicClock clock,
		int frames)
	{
		for (int index = 0; index < WarmupFrames; index++)
		{
			clock.AdvanceNanoseconds(16_666_667);
			loop.Advance();
		}

		long before = GC.GetAllocatedBytesForCurrentThread();

		for (int index = 0; index < frames; index++)
		{
			clock.AdvanceNanoseconds(16_666_667);
			loop.Advance();
		}

		long after = GC.GetAllocatedBytesForCurrentThread();

		Assert.Equal(before, after);
	}

	private static GameLoop CreateLoop(out FakeMonotonicClock clock)
	{
		clock = new FakeMonotonicClock();

		return new GameLoop(GameLoopSettings.Default, clock);
	}
}
