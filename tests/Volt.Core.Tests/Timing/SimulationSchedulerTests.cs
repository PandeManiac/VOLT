using Volt.Core.Timing;

namespace Volt.Core.Tests.Timing;

public sealed class SimulationSchedulerTests
{
	[Fact]
	public void EveryHz60_RunsOnEveryStep()
	{
		SimulationScheduler scheduler = CreateScheduler(out FakeClockProbe probe);

		scheduler.EveryHz(60, probe.Record);

		for (int step = 1; step <= 600; step++)
		{
			scheduler.Advance(step);
		}

		Assert.Equal(600, probe.Count);
		Assert.Equal(600, scheduler.TotalInvocations);
		Assert.Equal(
			Enumerable.Range(1, 600).Select(step => (long)step),
			probe.Steps);
	}

	[Fact]
	public void EveryHz30_RunsEveryOtherStep()
	{
		SimulationScheduler scheduler = CreateScheduler(out FakeClockProbe probe);

		scheduler.EveryHz(30, probe.Record);

		for (int step = 1; step <= 600; step++)
		{
			scheduler.Advance(step);
		}

		Assert.Equal(300, probe.Count);
		Assert.Equal(
			Enumerable.Range(1, 300).Select(step => (long)step * 2),
			probe.Steps);
	}

	[Fact]
	public void EveryHz20_RunsEveryThirdStep()
	{
		SimulationScheduler scheduler = CreateScheduler(out FakeClockProbe probe);

		scheduler.EveryHz(20, probe.Record);

		for (int step = 1; step <= 600; step++)
		{
			scheduler.Advance(step);
		}

		Assert.Equal(200, probe.Count);
		Assert.Equal(3, probe.Steps[0]);
		Assert.Equal(600, probe.Steps[^1]);
	}

	[Fact]
	public void EveryHz10_RunsEverySixthStep()
	{
		SimulationScheduler scheduler = CreateScheduler(out FakeClockProbe probe);

		scheduler.EveryHz(10, probe.Record);

		for (int step = 1; step <= 600; step++)
		{
			scheduler.Advance(step);
		}

		Assert.Equal(100, probe.Count);
		Assert.Equal(6, probe.Steps[0]);
		Assert.Equal(600, probe.Steps[^1]);
	}

	[Fact]
	public void EveryHz7_KeepsTheRationalCadence()
	{
		SimulationScheduler scheduler = CreateScheduler(out FakeClockProbe probe);

		scheduler.EveryHz(7, probe.Record);

		for (int step = 1; step <= 600; step++)
		{
			scheduler.Advance(step);
		}

		// 7/60 steps per occurrence is 8.571..., so whole-step
		// deadlines alternate 9, 9, 8, 9, 8, 9, 8 instead of a fixed
		// 8 steps (which would be 7.5 Hz).
		Assert.Equal(
			[9L, 18L, 26L, 35L, 43L, 52L, 60L, 69L],
			probe.Steps.Take(8));

		// Ten simulation seconds contain exactly seventy
		// invocations, not seventy-five.
		Assert.Equal(70, probe.Count);
		Assert.Equal(600, probe.Steps[^1]);
		Assert.Equal(0, scheduler.SkippedInvocations);
	}

	[Fact]
	public void EveryHz1_RunsEverySixtiethStep()
	{
		SimulationScheduler scheduler = CreateScheduler(out FakeClockProbe probe);

		scheduler.EveryHz(1, probe.Record);

		for (int step = 1; step <= 600; step++)
		{
			scheduler.Advance(step);
		}

		Assert.Equal(10, probe.Count);
		Assert.Equal(60, probe.Steps[0]);
		Assert.Equal(600, probe.Steps[^1]);
	}

	[Fact]
	public void Every750Milliseconds_RunsEvery45Steps()
	{
		SimulationScheduler scheduler = CreateScheduler(out FakeClockProbe probe);

		scheduler.Every(TimeSpan.FromMilliseconds(750), probe.Record);

		for (int step = 1; step <= 600; step++)
		{
			scheduler.Advance(step);
		}

		Assert.Equal(13, probe.Count);
		Assert.Equal(45, probe.Steps[0]);
		Assert.Equal(585, probe.Steps[^1]);
	}

	[Fact]
	public void After_FiresOnceAtTheRequestedStep()
	{
		SimulationScheduler scheduler = CreateScheduler(out FakeClockProbe probe);

		scheduler.After(TimeSpan.FromSeconds(5), probe.Record);

		Assert.Equal(1, scheduler.ActiveCount);
		Assert.Equal(300, scheduler.NextDeadlineStep);

		for (int step = 1; step <= 600; step++)
		{
			scheduler.Advance(step);
		}

		Assert.Equal([300L], probe.Steps);
		Assert.Equal(0, scheduler.ActiveCount);
		Assert.Equal(-1, scheduler.NextDeadlineStep);
	}

	[Fact]
	public void EqualDeadlines_RunInRegistrationOrder()
	{
		SimulationScheduler scheduler = CreateScheduler(out _);
		List<string> order = [];

		scheduler.EveryHz(1, (in ScheduledUpdateContext _) => order.Add("first"));
		scheduler.Every(TimeSpan.FromSeconds(1), (in ScheduledUpdateContext _) => order.Add("second"));
		scheduler.After(TimeSpan.FromSeconds(1), (in ScheduledUpdateContext _) => order.Add("third"));

		scheduler.Advance(60);

		Assert.Equal(["first", "second", "third"], order);
	}

	[Fact]
	public void Cancel_StopsFutureInvocations()
	{
		SimulationScheduler scheduler = CreateScheduler(out FakeClockProbe probe);

		ScheduledHandle handle = scheduler.EveryHz(1, probe.Record);

		scheduler.Advance(60);
		scheduler.Advance(120);

		Assert.True(scheduler.Cancel(handle));
		Assert.Equal(0, scheduler.ActiveCount);

		scheduler.Advance(180);
		scheduler.Advance(240);

		Assert.Equal([60L, 120L], probe.Steps);
		Assert.False(scheduler.Cancel(handle));
	}

	[Fact]
	public void CancelTwice_IsSafe()
	{
		SimulationScheduler scheduler = CreateScheduler(out _);

		ScheduledHandle handle = scheduler.EveryHz(10, (in ScheduledUpdateContext _) => { });

		Assert.True(scheduler.Cancel(handle));
		Assert.False(scheduler.Cancel(handle));
		Assert.False(scheduler.Cancel(ScheduledHandle.None));
		Assert.False(scheduler.Cancel(new ScheduledHandle(999, 1)));
	}

	[Fact]
	public void StaleHandle_DoesNotCancelAReusedSlot()
	{
		SimulationScheduler scheduler = CreateScheduler(out _);
		int first = 0;
		int second = 0;

		ScheduledHandle stale = scheduler.EveryHz(
			1,
			(in ScheduledUpdateContext _) => first++);

		Assert.True(scheduler.Cancel(stale));

		ScheduledHandle current = scheduler.EveryHz(
			1,
			(in ScheduledUpdateContext _) => second++);

		// The allocator reuses the freed slot; the stale handle must
		// not be able to cancel the new registration.
		Assert.False(scheduler.Cancel(stale));
		Assert.NotEqual(stale, current);

		scheduler.Advance(60);

		Assert.Equal(0, first);
		Assert.Equal(1, second);
	}

	[Fact]
	public void OneShot_IsRemovedAfterItsInvocation()
	{
		SimulationScheduler scheduler = CreateScheduler(out FakeClockProbe probe);

		ScheduledHandle handle = scheduler.After(
			TimeSpan.Zero,
			probe.Record);

		scheduler.Advance(1);
		scheduler.Advance(2);
		scheduler.Advance(3);

		Assert.Equal([1L], probe.Steps);
		Assert.Equal(0, scheduler.ActiveCount);
		Assert.False(scheduler.Cancel(handle));
	}

	[Fact]
	public void RegisterDuringDispatch_RunsOnTheNextStep()
	{
		SimulationScheduler scheduler = CreateScheduler(out _);
		List<string> order = [];

		scheduler.EveryHz(60, (in ScheduledUpdateContext _) =>
		{
			order.Add("first");

			if (order.Count == 1)
			{
				scheduler.After(
					TimeSpan.Zero,
					(in ScheduledUpdateContext context) =>
						order.Add($"second at {context.StepIndex}"));
			}
		});

		scheduler.Advance(1);

		Assert.Equal(["first"], order);

		scheduler.Advance(2);

		Assert.Equal(["first", "first", "second at 2"], order);
	}

	[Fact]
	public void CancelPendingWorkDuringDispatch_SkipsIt()
	{
		SimulationScheduler scheduler = CreateScheduler(out _);
		List<string> order = [];

		ScheduledHandle second = default;

		scheduler.EveryHz(1, (in ScheduledUpdateContext _) =>
		{
			order.Add("first");
			scheduler.Cancel(second);
		});

		second = scheduler.EveryHz(
			1,
			(in ScheduledUpdateContext _) => order.Add("second"));

		scheduler.Advance(60);

		Assert.Equal(["first"], order);
	}

	[Fact]
	public void CancelSelf_StopsFutureInvocations()
	{
		SimulationScheduler scheduler = CreateScheduler(out FakeClockProbe probe);
		List<int> count = [];
		ScheduledHandle handle = default;

		handle = scheduler.EveryHz(1, (in ScheduledUpdateContext context) =>
		{
			probe.Record(in context);
			count.Add(probe.Count);
			scheduler.Cancel(handle);
		});

		scheduler.Advance(60);
		scheduler.Advance(120);

		Assert.Equal([1], count);
		Assert.Equal(0, scheduler.ActiveCount);
	}

	[Fact]
	public void OverdueRecurringWork_RunsOnceAndSkipsMissedOccurrences()
	{
		SimulationScheduler scheduler = CreateScheduler(out FakeClockProbe probe);

		scheduler.EveryHz(1, probe.Record);

		// One thousand steps arrive as a single jump. The callback
		// runs once, not sixteen times.
		scheduler.Advance(1000);

		Assert.Equal([1000L], probe.Steps);
		Assert.Equal(15, scheduler.SkippedInvocations);

		// The next deadline follows the original 60-step schedule.
		Assert.Equal(1020, scheduler.NextDeadlineStep);

		scheduler.Advance(1019);
		Assert.Single(probe.Steps);

		scheduler.Advance(1020);
		Assert.Equal([1000L, 1020L], probe.Steps);
		Assert.Equal(0, probe.LastSkippedInvocations);
	}

	[Fact]
	public void HugeStepJump_DoesNotSpin()
	{
		SimulationScheduler scheduler = CreateScheduler(out FakeClockProbe probe);

		scheduler.EveryHz(60, probe.Record);

		scheduler.Advance(1_000_000);

		Assert.Single(probe.Steps);
		Assert.Equal(1_000_001, scheduler.NextDeadlineStep);
		Assert.True(scheduler.SkippedInvocations > 900_000);
	}

	[Fact]
	public void ScheduledContext_ReportsStepTimeAndInvocation()
	{
		SimulationScheduler scheduler = CreateScheduler(out _);
		List<ScheduledUpdateContext> contexts = [];

		scheduler.EveryHz(
			10,
			(in ScheduledUpdateContext context) =>
				contexts.Add(context));

		for (int step = 1; step <= 12; step++)
		{
			scheduler.Advance(step);
		}

		Assert.Equal(2, contexts.Count);
		Assert.Equal(6, contexts[0].StepIndex);
		Assert.Equal(0.1, contexts[0].SimulationTimeSeconds, 9);
		Assert.Equal(1, contexts[0].Invocation);
		Assert.Equal(0, contexts[0].SkippedInvocations);
		Assert.Equal(12, contexts[1].StepIndex);
		Assert.Equal(0.2, contexts[1].SimulationTimeSeconds, 9);
		Assert.Equal(2, contexts[1].Invocation);
	}

	[Fact]
	public void Callback_RunsSynchronouslyOnTheCallingThread()
	{
		SimulationScheduler scheduler = CreateScheduler(out _);
		int callingThread = Environment.CurrentManagedThreadId;
		int callbackThread = 0;

		scheduler.EveryHz(
			60,
			(in ScheduledUpdateContext _) =>
				callbackThread = Environment.CurrentManagedThreadId);

		scheduler.Advance(1);

		Assert.Equal(callingThread, callbackThread);
	}

	[Fact]
	public void ActiveCount_TracksRegistrations()
	{
		SimulationScheduler scheduler = CreateScheduler(out _);

		Assert.Equal(0, scheduler.ActiveCount);

		ScheduledHandle first = scheduler.EveryHz(
			10,
			(in ScheduledUpdateContext _) => { });
		scheduler.After(
			TimeSpan.FromSeconds(1),
			(in ScheduledUpdateContext _) => { });

		Assert.Equal(2, scheduler.ActiveCount);

		scheduler.Cancel(first);

		Assert.Equal(1, scheduler.ActiveCount);
		Assert.Equal(60, scheduler.NextDeadlineStep);
	}

	[Fact]
	public void RateAboveTheFixedRate_IsRejected()
	{
		SimulationScheduler scheduler = CreateScheduler(out _);

		ArgumentOutOfRangeException exception =
			Assert.Throws<ArgumentOutOfRangeException>(
				() => scheduler.EveryHz(
					144,
					(in ScheduledUpdateContext _) => { }));

		Assert.Contains("60 Hz", exception.Message);
	}

	[Fact]
	public void IntervalBelowOneFixedStep_IsRejected()
	{
		SimulationScheduler scheduler = CreateScheduler(out _);

		Assert.Throws<ArgumentOutOfRangeException>(
			() => scheduler.Every(
				TimeSpan.FromMilliseconds(16),
				(in ScheduledUpdateContext _) => { }));
	}

	[Theory]
	[InlineData(0, 0)]
	[InlineData(0, -1)]
	[InlineData(-1, 0)]
	public void NonPositiveIntervals_AreRejected(int seconds, int milliseconds)
	{
		SimulationScheduler scheduler = CreateScheduler(out _);

		Assert.Throws<ArgumentOutOfRangeException>(
			() => scheduler.Every(
				TimeSpan.FromSeconds(seconds) +
					TimeSpan.FromMilliseconds(milliseconds),
				(in ScheduledUpdateContext _) => { }));
	}

	[Fact]
	public void NegativeDelay_IsRejected()
	{
		SimulationScheduler scheduler = CreateScheduler(out _);

		Assert.Throws<ArgumentOutOfRangeException>(
			() => scheduler.After(
				TimeSpan.FromMilliseconds(-1),
				(in ScheduledUpdateContext _) => { }));
	}

	[Theory]
	[InlineData(0)]
	[InlineData(-5)]
	public void NonPositiveFrequencies_AreRejected(int frequency)
	{
		SimulationScheduler scheduler = CreateScheduler(out _);

		Assert.Throws<ArgumentOutOfRangeException>(
			() => scheduler.EveryHz(
				frequency,
				(in ScheduledUpdateContext _) => { }));
	}

	[Fact]
	public void NullCallbacks_AreRejected()
	{
		SimulationScheduler scheduler = CreateScheduler(out _);

		Assert.Throws<ArgumentNullException>(
			() => scheduler.EveryHz(10, null!));
		Assert.Throws<ArgumentNullException>(
			() => scheduler.Every(TimeSpan.FromSeconds(1), null!));
		Assert.Throws<ArgumentNullException>(
			() => scheduler.After(TimeSpan.FromSeconds(1), null!));
	}

	private static SimulationScheduler CreateScheduler(
		out FakeClockProbe probe,
		int fixedUpdatesPerSecond = 60)
	{
		probe = new FakeClockProbe();

		return new SimulationScheduler(fixedUpdatesPerSecond);
	}

	private sealed class FakeClockProbe
	{
		public List<long> Steps { get; } = [];

		public int Count => Steps.Count;

		public long LastSkippedInvocations { get; private set; }

		public void Record(in ScheduledUpdateContext context)
		{
			Steps.Add(context.StepIndex);
			LastSkippedInvocations = context.SkippedInvocations;
		}
	}
}
