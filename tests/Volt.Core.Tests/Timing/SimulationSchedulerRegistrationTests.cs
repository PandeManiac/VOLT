using Volt.Core.Timing;

namespace Volt.Core.Tests.Timing;

/// <summary>
/// Covers deadlines measured from the fixed step at registration, so a
/// late registration is not pulled forward to the next step.
/// </summary>
public sealed class SimulationSchedulerRegistrationTests
{
	[Fact]
	public void After_FiveSeconds_RegisteredAfterStep400_FiresAtStep700()
	{
		SimulationScheduler scheduler = CreateScheduler();
		List<long> invocations = [];
		long step = 0;

		Advance(scheduler, ref step, 400);

		scheduler.After(
			TimeSpan.FromSeconds(5),
			(in ScheduledUpdateContext context) =>
				invocations.Add(context.StepIndex));

		Assert.Equal(700, scheduler.NextDeadlineStep);
		Assert.Equal(1, scheduler.ActiveCount);

		// Steps 401 through 699 must not run the callback.
		Advance(scheduler, ref step, 299);

		Assert.Empty(invocations);
		Assert.Equal(700, scheduler.NextDeadlineStep);

		Advance(scheduler, ref step, 1);

		Assert.Equal([700L], invocations);
		Assert.Equal(0, scheduler.ActiveCount);
		Assert.Equal(-1, scheduler.NextDeadlineStep);
	}

	[Fact]
	public void After_Zero_RegisteredAfterStep400_FiresAtStep401()
	{
		SimulationScheduler scheduler = CreateScheduler();
		List<long> invocations = [];
		long step = 0;

		Advance(scheduler, ref step, 400);

		scheduler.After(
			TimeSpan.Zero,
			(in ScheduledUpdateContext context) =>
				invocations.Add(context.StepIndex));

		Assert.Equal(401, scheduler.NextDeadlineStep);

		Advance(scheduler, ref step, 1);

		Assert.Equal([401L], invocations);
	}

	[Fact]
	public void EveryHz_Ten_RegisteredAfterStep120_FirstFiresAtStep126()
	{
		SimulationScheduler scheduler = CreateScheduler();
		List<long> invocations = [];
		long step = 0;

		Advance(scheduler, ref step, 120);

		scheduler.EveryHz(10, (in ScheduledUpdateContext context) =>
			invocations.Add(context.StepIndex));

		Assert.Equal(126, scheduler.NextDeadlineStep);

		Advance(scheduler, ref step, 5);

		Assert.Empty(invocations);

		Advance(scheduler, ref step, 1);

		Assert.Equal([126L], invocations);
		Assert.Equal(132, scheduler.NextDeadlineStep);
	}

	[Fact]
	public void Every_TwoSeconds_RegisteredAfterStep120_FirstFiresAtStep240()
	{
		SimulationScheduler scheduler = CreateScheduler();
		List<long> invocations = [];
		long step = 0;

		Advance(scheduler, ref step, 120);

		scheduler.Every(
			TimeSpan.FromSeconds(2),
			(in ScheduledUpdateContext context) =>
				invocations.Add(context.StepIndex));

		Assert.Equal(240, scheduler.NextDeadlineStep);

		Advance(scheduler, ref step, 119);

		Assert.Empty(invocations);

		Advance(scheduler, ref step, 1);

		Assert.Equal([240L], invocations);
		Assert.Equal(360, scheduler.NextDeadlineStep);
	}

	[Fact]
	public void EveryHz_Seven_RegisteredAfterStep100_KeepsItsCadence()
	{
		SimulationScheduler scheduler = CreateScheduler();
		List<long> invocations = [];
		long step = 0;

		Advance(scheduler, ref step, 100);

		scheduler.EveryHz(7, (in ScheduledUpdateContext context) =>
			invocations.Add(context.StepIndex));

		Assert.Equal(109, scheduler.NextDeadlineStep);

		// Ten simulation seconds after registration contain exactly
		// seventy invocations, not seventy-five.
		Advance(scheduler, ref step, 600);

		Assert.Equal(70, invocations.Count);
		Assert.Equal(109, invocations[0]);
		Assert.Equal(700, invocations[^1]);
		Assert.Equal(0, scheduler.SkippedInvocations);

		// One hundred simulation seconds contain exactly seven
		// hundred invocations, so the origin does not shift the
		// long-run average.
		Advance(scheduler, ref step, 5_400);

		Assert.Equal(700, invocations.Count);
		Assert.Equal(6_100, invocations[^1]);
		Assert.Equal(0, scheduler.SkippedInvocations);
	}

	[Fact]
	public void EqualDeadlinesAfterLateRegistration_KeepRegistrationOrder()
	{
		SimulationScheduler scheduler = CreateScheduler();
		List<string> order = [];
		long step = 0;

		Advance(scheduler, ref step, 400);

		scheduler.After(
			TimeSpan.FromSeconds(1),
			(in ScheduledUpdateContext _) => order.Add("first"));
		scheduler.After(
			TimeSpan.FromSeconds(1),
			(in ScheduledUpdateContext _) => order.Add("second"));
		scheduler.EveryHz(
			1,
			(in ScheduledUpdateContext _) => order.Add("third"));

		Assert.Equal(460, scheduler.NextDeadlineStep);

		Advance(scheduler, ref step, 60);

		Assert.Equal(["first", "second", "third"], order);
	}

	[Fact]
	public void Cancel_StopsALateRegistration()
	{
		SimulationScheduler scheduler = CreateScheduler();
		int invocations = 0;
		long step = 0;

		Advance(scheduler, ref step, 400);

		ScheduledHandle handle = scheduler.After(
			TimeSpan.FromSeconds(5),
			(in ScheduledUpdateContext _) => invocations++);

		Assert.True(scheduler.Cancel(handle));
		Assert.Equal(0, scheduler.ActiveCount);
		Assert.Equal(-1, scheduler.NextDeadlineStep);

		Advance(scheduler, ref step, 400);

		Assert.Equal(0, invocations);
		Assert.False(scheduler.Cancel(handle));
	}

	[Fact]
	public void StaleHandle_CannotCancelAReusedSlotAfterLateRegistration()
	{
		SimulationScheduler scheduler = CreateScheduler();
		int first = 0;
		int second = 0;
		long step = 0;

		Advance(scheduler, ref step, 400);

		ScheduledHandle stale = scheduler.After(
			TimeSpan.FromSeconds(1),
			(in ScheduledUpdateContext _) => first++);

		Assert.True(scheduler.Cancel(stale));

		ScheduledHandle current = scheduler.After(
			TimeSpan.FromSeconds(1),
			(in ScheduledUpdateContext _) => second++);

		// The allocator reuses the freed slot; the stale handle must
		// not be able to cancel the new registration.
		Assert.NotEqual(stale, current);
		Assert.False(scheduler.Cancel(stale));

		Advance(scheduler, ref step, 60);

		Assert.Equal(0, first);
		Assert.Equal(1, second);
	}

	[Fact]
	public void RegistrationInsideDispatch_DoesNotRunInTheSameStep()
	{
		SimulationScheduler scheduler = CreateScheduler();
		List<string> order = [];
		long step = 0;

		scheduler.EveryHz(1, (in ScheduledUpdateContext _) =>
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

		// Step 60 is due; the registration made there starts at 61.
		Advance(scheduler, ref step, 60);

		Assert.Equal(["first"], order);
		Assert.Equal(61, scheduler.NextDeadlineStep);

		Advance(scheduler, ref step, 1);

		Assert.Equal(["first", "second at 61"], order);
	}

	private static SimulationScheduler CreateScheduler()
	{
		return new SimulationScheduler(60);
	}

	private static void Advance(
		SimulationScheduler scheduler,
		ref long step,
		int count)
	{
		for (int index = 0; index < count; index++)
		{
			step++;
			scheduler.Advance(step);
		}
	}
}
