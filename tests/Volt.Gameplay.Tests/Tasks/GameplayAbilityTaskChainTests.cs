using Volt.Ecs;
using Volt.Gameplay.Abilities;
using Volt.Gameplay.Tasks;
using Volt.Gameplay.Tests.Abilities;

namespace Volt.Gameplay.Tests.Tasks;

/// <summary>
/// Pins the documented temporal semantics of tasks created by a
/// completion callback.
/// </summary>
/// <remarks>
/// Completions run at the end-of-step clock. A task created there is
/// eligible on the next advance, matching <c>SimulationScheduler</c>'s
/// rule that work registered during a step cannot run in that step.
/// Chained waits are therefore deliberately not independent of how a
/// large step is subdivided.
/// </remarks>
public sealed class GameplayAbilityTaskChainTests
{
	[Fact]
	public void OneLargeAdvance_DefersTheTaskCreatedByACompletion()
	{
		using AbilityRuntime runtime = new();
		Entity actor = runtime.CreateActor();

		GameplayAbilityActivation activation =
			TaskTestSupport.ActivateChannel(runtime, actor, out _);

		GameplayAbilityTaskHandle first =
			runtime.Tasks.WaitDuration(activation, 1f);

		GameplayAbilityTaskHandle second =
			GameplayAbilityTaskHandle.None;

		runtime.Tasks.Changed += change =>
		{
			if (change.Kind == GameplayAbilityTaskChangeKind.Completed
				&& change.Handle == first)
			{
				second = runtime.Tasks.WaitDuration(activation, 1f);
			}
		};

		runtime.Tasks.Advance(10f);

		Assert.False(runtime.Tasks.TryGetTask(first, out _));
		Assert.False(second.IsNone);
		Assert.True(runtime.Tasks.TryGetTask(second, out _));
		Assert.Equal(1, runtime.Tasks.PendingTaskCount);
		Assert.Equal(10d, runtime.Tasks.Time);

		runtime.Tasks.Advance(1f);

		Assert.False(runtime.Tasks.TryGetTask(second, out _));
		Assert.Equal(0, runtime.Tasks.PendingTaskCount);
		Assert.Equal(11d, runtime.Tasks.Time);
	}

	[Fact]
	public void SubdividedAdvances_RunEachChainedLinkInItsOwnAdvance()
	{
		using AbilityRuntime runtime = new();
		Entity actor = runtime.CreateActor();

		GameplayAbilityActivation activation =
			TaskTestSupport.ActivateChannel(runtime, actor, out _);

		GameplayAbilityTaskHandle first =
			runtime.Tasks.WaitDuration(activation, 1f);

		GameplayAbilityTaskHandle second =
			GameplayAbilityTaskHandle.None;

		runtime.Tasks.Changed += change =>
		{
			if (change.Kind == GameplayAbilityTaskChangeKind.Completed
				&& change.Handle == first)
			{
				second = runtime.Tasks.WaitDuration(activation, 1f);
			}
		};

		runtime.Tasks.Advance(1f);

		Assert.False(second.IsNone);
		Assert.True(runtime.Tasks.TryGetTask(second, out _));
		Assert.Equal(1, runtime.Tasks.PendingTaskCount);

		runtime.Tasks.Advance(9f);

		Assert.False(runtime.Tasks.TryGetTask(second, out _));
		Assert.Equal(0, runtime.Tasks.PendingTaskCount);
		Assert.Equal(10d, runtime.Tasks.Time);
	}

	[Fact]
	public void ZeroDurationTaskCreatedByACompletion_DoesNotRunInTheSameAdvance()
	{
		using AbilityRuntime runtime = new();
		Entity actor = runtime.CreateActor();

		GameplayAbilityActivation activation =
			TaskTestSupport.ActivateChannel(runtime, actor, out _);

		GameplayAbilityTaskHandle first =
			runtime.Tasks.WaitDuration(activation, 1f);

		GameplayAbilityTaskHandle zero =
			GameplayAbilityTaskHandle.None;

		runtime.Tasks.Changed += change =>
		{
			if (change.Kind == GameplayAbilityTaskChangeKind.Completed
				&& change.Handle == first)
			{
				zero = runtime.Tasks.WaitDuration(activation, 0f);
			}
		};

		runtime.Tasks.Advance(10f);

		Assert.False(zero.IsNone);
		Assert.True(runtime.Tasks.TryGetTask(zero, out _));
		Assert.Equal(1, runtime.Tasks.PendingTaskCount);

		// Zero time is a no-op, so the deferred task still waits.
		runtime.Tasks.Advance(0f);

		Assert.True(runtime.Tasks.TryGetTask(zero, out _));

		runtime.Tasks.Advance(0.001f);

		Assert.False(runtime.Tasks.TryGetTask(zero, out _));
		Assert.Equal(0, runtime.Tasks.PendingTaskCount);
	}

	[Fact]
	public void ZeroDurationTaskCreatedDuringACompletion_DoesNotStarveEqualDeadlines()
	{
		using AbilityRuntime runtime = new();
		Entity actor = runtime.CreateActor();

		GameplayAbilityActivation activation =
			TaskTestSupport.ActivateChannel(runtime, actor, out _);

		GameplayAbilityTaskHandle first =
			runtime.Tasks.WaitDuration(activation, 1f);

		GameplayAbilityTaskHandle equalDeadline =
			runtime.Tasks.WaitDuration(activation, 1f);

		GameplayAbilityTaskHandle deferred =
			GameplayAbilityTaskHandle.None;

		runtime.Tasks.Changed += change =>
		{
			if (change.Kind == GameplayAbilityTaskChangeKind.Completed
				&& change.Handle == first)
			{
				deferred = runtime.Tasks.WaitDuration(activation, 0f);
			}
		};

		runtime.Tasks.Advance(1f);

		// The equal-deadline task was pending before the advance and
		// must run. The zero task created by the callback is deferred
		// and sits just past the boundary, not at the heap root.
		Assert.False(runtime.Tasks.TryGetTask(equalDeadline, out _));
		Assert.False(deferred.IsNone);
		Assert.True(runtime.Tasks.TryGetTask(deferred, out _));
		Assert.Equal(1, runtime.Tasks.PendingTaskCount);

		runtime.Tasks.Advance(0.001f);

		Assert.False(runtime.Tasks.TryGetTask(deferred, out _));
		Assert.Equal(0, runtime.Tasks.PendingTaskCount);
	}

	[Fact]
	public void ManyChainedZeroTasks_DoNotRunWithoutFurtherAdvances()
	{
		using AbilityRuntime runtime = new();
		Entity actor = runtime.CreateActor();

		GameplayAbilityActivation activation =
			TaskTestSupport.ActivateChannel(runtime, actor, out _);

		GameplayAbilityTaskHandle first =
			runtime.Tasks.WaitDuration(activation, 0f);

		int created = 0;

		runtime.Tasks.Changed += change =>
		{
			if (change.Kind == GameplayAbilityTaskChangeKind.Completed
				&& created < 10_000)
			{
				created++;
				runtime.Tasks.WaitDuration(activation, 0f);
			}
		};

		runtime.Tasks.Advance(0.001f);

		Assert.Equal(1, created);
		Assert.Equal(1, runtime.Tasks.PendingTaskCount);

		runtime.Tasks.Advance(0.001f);

		Assert.Equal(2, created);
		Assert.Equal(1, runtime.Tasks.PendingTaskCount);
	}

	[Fact]
	public void TasksCreatedBeforeTheAdvance_AllCompleteInDeadlineOrder()
	{
		using AbilityRuntime runtime = new();
		Entity actor = runtime.CreateActor();

		GameplayAbilityActivation activation =
			TaskTestSupport.ActivateChannel(runtime, actor, out _);

		GameplayAbilityTaskHandle late =
			runtime.Tasks.WaitDuration(activation, 2f);

		GameplayAbilityTaskHandle early =
			runtime.Tasks.WaitDuration(activation, 1f);

		GameplayAbilityTaskHandle equal =
			runtime.Tasks.WaitDuration(activation, 1f);

		List<GameplayAbilityTaskHandle> completed = [];

		runtime.Tasks.Changed += change =>
		{
			if (change.Kind == GameplayAbilityTaskChangeKind.Completed)
			{
				completed.Add(change.Handle);
			}
		};

		runtime.Tasks.Advance(10f);

		Assert.Equal(3, completed.Count);
		Assert.Equal(early, completed[0]);
		Assert.Equal(equal, completed[1]);
		Assert.Equal(late, completed[2]);
		Assert.Equal(0, runtime.Tasks.PendingTaskCount);
	}
}
