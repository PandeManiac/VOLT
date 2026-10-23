using Volt.Ecs;
using Volt.Gameplay.Abilities;
using Volt.Gameplay.Tasks;
using Volt.Gameplay.Tests.Abilities;

namespace Volt.Gameplay.Tests.Tasks;

/// <summary>
/// Covers the simulation clock, deadline ordering, and zero-duration
/// semantics of duration tasks.
/// </summary>
public sealed class GameplayAbilityTaskDurationTests
{
	[Fact]
	public void WaitDuration_CompletesAtItsDeadline()
	{
		using AbilityRuntime runtime = new();
		TaskRecorder recorder = new(runtime.Tasks);
		Entity actor = runtime.CreateActor();

		GameplayAbilityActivation activation =
			TaskTestSupport.ActivateChannel(runtime, actor, out _);

		GameplayAbilityTaskHandle task =
			runtime.Tasks.WaitDuration(activation, 0.5f);

		Assert.False(task.IsNone);
		Assert.Equal(1, runtime.Tasks.PendingTaskCount);
		Assert.True(runtime.Tasks.TryGetTask(task, out var info));
		Assert.Equal(0.5, info.RemainingTime, 6);
		Assert.Equal(1, recorder.Count(GameplayAbilityTaskChangeKind.Started));

		runtime.Tasks.Advance(0.49f);

		Assert.Equal(1, runtime.Tasks.PendingTaskCount);
		Assert.Equal(
			0,
			recorder.Count(GameplayAbilityTaskChangeKind.Completed));

		runtime.Tasks.Advance(0.02f);

		Assert.Equal(0, runtime.Tasks.PendingTaskCount);
		Assert.Equal(
			1,
			recorder.Count(GameplayAbilityTaskChangeKind.Completed));

		GameplayAbilityTaskChange change =
			recorder.Last(GameplayAbilityTaskChangeKind.Completed);

		Assert.Equal(task, change.Handle);
		Assert.Equal(activation, change.Activation);
		Assert.Equal(GameplayAbilityTaskKind.Duration, change.TaskKind);
		Assert.Equal(
			GameplayAbilityTaskCompletionReason.Elapsed,
			change.Reason);

		Assert.False(runtime.Tasks.TryGetTask(task, out _));
		Assert.False(runtime.Tasks.Cancel(task));
		Assert.False(runtime.Tasks.Complete(task));
	}

	[Fact]
	public void ZeroDuration_CompletesOnTheNextPositiveAdvance()
	{
		using AbilityRuntime runtime = new();
		TaskRecorder recorder = new(runtime.Tasks);
		Entity actor = runtime.CreateActor();

		GameplayAbilityActivation activation =
			TaskTestSupport.ActivateChannel(runtime, actor, out _);

		GameplayAbilityTaskHandle task =
			runtime.Tasks.WaitDuration(activation, 0f);

		Assert.False(task.IsNone);
		Assert.Equal(
			0,
			recorder.Count(GameplayAbilityTaskChangeKind.Completed));

		runtime.Tasks.Advance(0f);

		Assert.Equal(1, runtime.Tasks.PendingTaskCount);
		Assert.Equal(
			0,
			recorder.Count(GameplayAbilityTaskChangeKind.Completed));

		runtime.Tasks.Advance(0.001f);

		Assert.Equal(0, runtime.Tasks.PendingTaskCount);
		Assert.Equal(
			1,
			recorder.Count(GameplayAbilityTaskChangeKind.Completed));
	}

	[Fact]
	public void MultipleEqualDeadlines_CompleteInCreationOrder()
	{
		using AbilityRuntime runtime = new();
		TaskRecorder recorder = new(runtime.Tasks);
		Entity actor = runtime.CreateActor();

		GameplayAbilityActivation activation =
			TaskTestSupport.ActivateChannel(runtime, actor, out _);

		GameplayAbilityTaskHandle first =
			runtime.Tasks.WaitDuration(activation, 1f);
		GameplayAbilityTaskHandle second =
			runtime.Tasks.WaitDuration(activation, 1f);
		GameplayAbilityTaskHandle third =
			runtime.Tasks.WaitDuration(activation, 1f);

		runtime.Tasks.Advance(1f);

		GameplayAbilityTaskChange[] completed = recorder.Changes
			.Where(change =>
				change.Kind == GameplayAbilityTaskChangeKind.Completed)
			.ToArray();

		Assert.Equal(3, completed.Length);
		Assert.Equal(first, completed[0].Handle);
		Assert.Equal(second, completed[1].Handle);
		Assert.Equal(third, completed[2].Handle);
	}

	[Fact]
	public void LargeAdvance_CompletesInDeadlineOrder()
	{
		using AbilityRuntime runtime = new();
		TaskRecorder recorder = new(runtime.Tasks);
		Entity actor = runtime.CreateActor();

		GameplayAbilityActivation activation =
			TaskTestSupport.ActivateChannel(runtime, actor, out _);

		GameplayAbilityTaskHandle late =
			runtime.Tasks.WaitDuration(activation, 3f);
		GameplayAbilityTaskHandle early =
			runtime.Tasks.WaitDuration(activation, 1f);
		GameplayAbilityTaskHandle middle =
			runtime.Tasks.WaitDuration(activation, 2f);

		runtime.Tasks.Advance(10f);

		GameplayAbilityTaskHandle[] order = recorder.Changes
			.Where(change =>
				change.Kind == GameplayAbilityTaskChangeKind.Completed)
			.Select(change => change.Handle)
			.ToArray();

		Assert.Equal([early, middle, late], order);
		Assert.Equal(10d, runtime.Tasks.Time, 6);
	}

	[Fact]
	public void ZeroAdvance_IsAPauseAndDoesNoWork()
	{
		using AbilityRuntime runtime = new();
		TaskRecorder recorder = new(runtime.Tasks);
		Entity actor = runtime.CreateActor();

		GameplayAbilityActivation activation =
			TaskTestSupport.ActivateChannel(runtime, actor, out _);

		runtime.Tasks.WaitDuration(activation, 1f);

		runtime.Tasks.Advance(0f);
		runtime.Tasks.Advance(0f);

		Assert.Equal(1, runtime.Tasks.PendingTaskCount);
		Assert.Equal(0d, runtime.Tasks.Time);

		runtime.Tasks.Advance(1f);

		Assert.Equal(0, runtime.Tasks.PendingTaskCount);
	}

	[Fact]
	public void TaskCreatedDuringAdvance_IsDeferredToTheNextAdvance()
	{
		using AbilityRuntime runtime = new();
		TaskRecorder recorder = new(runtime.Tasks);
		Entity actor = runtime.CreateActor();

		GameplayAbilityActivation activation =
			TaskTestSupport.ActivateChannel(runtime, actor, out _);

		GameplayAbilityTaskHandle nested =
			GameplayAbilityTaskHandle.None;
		bool created = false;

		runtime.Tasks.Changed += change =>
		{
			if (change.Kind == GameplayAbilityTaskChangeKind.Completed
				&& !created)
			{
				created = true;

				nested = runtime.Tasks.WaitDuration(activation, 0f);
			}
		};

		runtime.Tasks.WaitDuration(activation, 0.5f);

		runtime.Tasks.Advance(0.5f);

		Assert.True(created);
		Assert.False(nested.IsNone);
		Assert.Equal(1, runtime.Tasks.PendingTaskCount);
		Assert.Equal(
			2,
			recorder.Count(GameplayAbilityTaskChangeKind.Started));

		runtime.Tasks.Advance(0.001f);

		Assert.Equal(0, runtime.Tasks.PendingTaskCount);
		Assert.Equal(
			2,
			recorder.Count(GameplayAbilityTaskChangeKind.Completed));
	}

	[Fact]
	public void NegativeOrNonFiniteDuration_Throws()
	{
		using AbilityRuntime runtime = new();
		Entity actor = runtime.CreateActor();

		GameplayAbilityActivation activation =
			TaskTestSupport.ActivateChannel(runtime, actor, out _);

		Assert.Throws<ArgumentOutOfRangeException>(
			() => runtime.Tasks.WaitDuration(activation, -1f));
		Assert.Throws<ArgumentOutOfRangeException>(
			() => runtime.Tasks.WaitDuration(activation, float.NaN));
		Assert.Throws<ArgumentOutOfRangeException>(
			() => runtime.Tasks.WaitDuration(
				activation,
				float.PositiveInfinity));
		Assert.Equal(0, runtime.Tasks.PendingTaskCount);
	}

	[Fact]
	public void NegativeOrNonFiniteAdvance_Throws()
	{
		using AbilityRuntime runtime = new();

		Assert.Throws<ArgumentOutOfRangeException>(
			() => runtime.Tasks.Advance(-0.1f));
		Assert.Throws<ArgumentOutOfRangeException>(
			() => runtime.Tasks.Advance(float.NaN));
		Assert.Throws<ArgumentOutOfRangeException>(
			() => runtime.Tasks.Advance(float.PositiveInfinity));
	}

	[Fact]
	public void WaitDuration_AfterTheAbilityEnded_ReturnsNone()
	{
		using AbilityRuntime runtime = new();
		Entity actor = runtime.CreateActor();

		GameplayAbilityActivation activation =
			TaskTestSupport.ActivateChannel(
				runtime,
				actor,
				out GameplayAbilityHandle handle);

		Assert.True(runtime.Abilities.End(handle));

		GameplayAbilityTaskHandle task =
			runtime.Tasks.WaitDuration(activation, 1f);

		Assert.True(task.IsNone);
		Assert.Equal(0, runtime.Tasks.PendingTaskCount);
	}

	[Fact]
	public void ManyIdleTasks_OnlyTheEarliestDeadlineIsVisited()
	{
		using AbilityRuntime runtime = new();
		TaskRecorder recorder = new(runtime.Tasks);
		Entity actor = runtime.CreateActor();

		GameplayAbilityActivation activation =
			TaskTestSupport.ActivateChannel(runtime, actor, out _);

		const int TaskCount = 1_000;

		GameplayAbilityTaskHandle first =
			runtime.Tasks.WaitDuration(activation, 1f);

		for (int index = 1; index < TaskCount; index++)
		{
			Assert.False(runtime.Tasks.WaitDuration(
				activation,
				1_000f + index).IsNone);
		}

		for (int index = 0; index < 128; index++)
		{
			runtime.Tasks.Advance(0.001f);
		}

		Assert.Equal(
			0,
			recorder.Count(GameplayAbilityTaskChangeKind.Completed));
		Assert.Equal(TaskCount, runtime.Tasks.PendingTaskCount);

		runtime.Tasks.Advance(1f);

		Assert.Equal(
			1,
			recorder.Count(GameplayAbilityTaskChangeKind.Completed));
		Assert.Equal(first, recorder.Last(
			GameplayAbilityTaskChangeKind.Completed).Handle);
		Assert.Equal(TaskCount - 1, runtime.Tasks.PendingTaskCount);
	}
}
