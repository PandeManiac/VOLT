using Volt.Ecs;
using Volt.Gameplay.Abilities;
using Volt.Gameplay.Events;
using Volt.Gameplay.Tasks;
using Volt.Gameplay.Tests.Abilities;
using Volt.Gameplay.Tests.Events;

namespace Volt.Gameplay.Tests.Tasks;

/// <summary>
/// Covers event-driven task completion, matching, filtering, and the
/// cleanup that makes a stale subscription harmless.
/// </summary>
public sealed class GameplayAbilityTaskEventTests
{
	[Fact]
	public void WaitEvent_CompletesSynchronouslyAndCarriesTheEvent()
	{
		using AbilityRuntime runtime = new();
		TaskRecorder recorder = new(runtime.Tasks);
		Entity actor = runtime.CreateActor();
		Entity target = runtime.CreateActor();

		GameplayAbilityActivation activation =
			TaskTestSupport.ActivateChannel(runtime, actor, out _);

		GameplayAbilityTaskHandle task = runtime.Tasks.WaitEvent(
			activation,
			EventSchema.AttackHit);

		Assert.False(task.IsNone);
		Assert.Equal(1, runtime.Tasks.PendingTaskCount);

		int delivered = runtime.Events.Publish(new GameplayEvent(
			EventSchema.AttackHit,
			actor,
			target,
			GameplayEventPayload.FromNumber(17d)));

		Assert.Equal(1, delivered);
		Assert.Equal(0, runtime.Tasks.PendingTaskCount);
		Assert.Equal(
			1,
			recorder.Count(GameplayAbilityTaskChangeKind.Completed));

		GameplayAbilityTaskChange change =
			recorder.Last(GameplayAbilityTaskChangeKind.Completed);

		Assert.Equal(task, change.Handle);
		Assert.Equal(
			GameplayAbilityTaskCompletionReason.ConditionMet,
			change.Reason);
		Assert.Equal(EventSchema.AttackHit, change.Event.Tag);
		Assert.Equal(actor, change.Event.Source);
		Assert.Equal(target, change.Event.Target);
		Assert.Equal(17d, change.Event.Payload.Number);
		Assert.False(runtime.Tasks.TryGetTask(task, out _));
	}

	[Fact]
	public void WaitEvent_IgnoresUnrelatedEvents()
	{
		using AbilityRuntime runtime = new();
		TaskRecorder recorder = new(runtime.Tasks);
		Entity actor = runtime.CreateActor();

		GameplayAbilityActivation activation =
			TaskTestSupport.ActivateChannel(runtime, actor, out _);

		runtime.Tasks.WaitEvent(activation, EventSchema.AttackHit);

		Assert.Equal(0, runtime.Events.Publish(new GameplayEvent(
			EventSchema.DamageReceived)));

		Assert.Equal(1, runtime.Tasks.PendingTaskCount);
		Assert.Equal(
			0,
			recorder.Count(GameplayAbilityTaskChangeKind.Completed));
	}

	[Fact]
	public void WaitEvent_MatchesExactlyOrHierarchically()
	{
		using AbilityRuntime runtime = new();
		TaskRecorder recorder = new(runtime.Tasks);
		Entity actor = runtime.CreateActor();

		GameplayAbilityActivation activation =
			TaskTestSupport.ActivateChannel(runtime, actor, out _);

		GameplayAbilityTaskHandle exact = runtime.Tasks.WaitEvent(
			activation,
			EventSchema.Attack);

		GameplayAbilityTaskHandle hierarchical = runtime.Tasks.WaitEvent(
			activation,
			EventSchema.Attack,
			GameplayEventMatch.Hierarchical);

		Assert.Equal(1, runtime.Events.Publish(new GameplayEvent(
			EventSchema.AttackHit)));

		Assert.Equal(1, runtime.Tasks.PendingTaskCount);
		Assert.True(runtime.Tasks.TryGetTask(exact, out _));
		Assert.False(runtime.Tasks.TryGetTask(hierarchical, out _));
		Assert.Equal(
			1,
			recorder.Count(GameplayAbilityTaskChangeKind.Completed));
	}

	[Fact]
	public void WaitEvent_FiltersBySourceAndTarget()
	{
		using AbilityRuntime runtime = new();
		Entity actor = runtime.CreateActor();
		Entity victim = runtime.CreateActor();
		Entity other = runtime.CreateActor();

		GameplayAbilityActivation activation =
			TaskTestSupport.ActivateChannel(runtime, actor, out _);

		GameplayAbilityTaskHandle bySource = runtime.Tasks.WaitEvent(
			activation,
			EventSchema.AttackHit,
			source: actor);

		GameplayAbilityTaskHandle byTarget = runtime.Tasks.WaitEvent(
			activation,
			EventSchema.AttackHit,
			target: victim);

		Assert.Equal(2, runtime.Events.Publish(new GameplayEvent(
			EventSchema.AttackHit,
			actor,
			victim)));

		Assert.Equal(0, runtime.Tasks.PendingTaskCount);
		Assert.False(runtime.Tasks.TryGetTask(bySource, out _));
		Assert.False(runtime.Tasks.TryGetTask(byTarget, out _));

		GameplayAbilityTaskHandle wrongSource = runtime.Tasks.WaitEvent(
			activation,
			EventSchema.AttackHit,
			source: actor);

		Assert.Equal(0, runtime.Events.Publish(new GameplayEvent(
			EventSchema.AttackHit,
			other,
			victim)));
		Assert.Equal(1, runtime.Tasks.PendingTaskCount);
		Assert.True(runtime.Tasks.TryGetTask(wrongSource, out _));
	}

	[Fact]
	public void MultipleTasksForOneEvent_AllCompleteNewestFirst()
	{
		using AbilityRuntime runtime = new();
		TaskRecorder recorder = new(runtime.Tasks);
		Entity actor = runtime.CreateActor();

		GameplayAbilityActivation activation =
			TaskTestSupport.ActivateChannel(runtime, actor, out _);

		GameplayAbilityTaskHandle first = runtime.Tasks.WaitEvent(
			activation,
			EventSchema.AttackHit);
		GameplayAbilityTaskHandle second = runtime.Tasks.WaitEvent(
			activation,
			EventSchema.AttackHit);
		GameplayAbilityTaskHandle third = runtime.Tasks.WaitEvent(
			activation,
			EventSchema.AttackHit);

		Assert.Equal(3, runtime.Events.Publish(new GameplayEvent(
			EventSchema.AttackHit)));

		GameplayAbilityTaskHandle[] order = recorder.Changes
			.Where(change =>
				change.Kind == GameplayAbilityTaskChangeKind.Completed)
			.Select(change => change.Handle)
			.ToArray();

		Assert.Equal([third, second, first], order);
		Assert.Equal(0, runtime.Tasks.PendingTaskCount);
	}

	[Fact]
	public void CancellingAWaitingTask_RemovesItsSubscription()
	{
		using AbilityRuntime runtime = new();
		TaskRecorder recorder = new(runtime.Tasks);
		Entity actor = runtime.CreateActor();

		GameplayAbilityActivation activation =
			TaskTestSupport.ActivateChannel(runtime, actor, out _);

		GameplayAbilityTaskHandle task = runtime.Tasks.WaitEvent(
			activation,
			EventSchema.AttackHit);

		Assert.True(runtime.Tasks.Cancel(task));
		Assert.False(runtime.Tasks.Cancel(task));
		Assert.Equal(0, runtime.Tasks.PendingTaskCount);

		GameplayAbilityTaskChange change =
			recorder.Last(GameplayAbilityTaskChangeKind.Cancelled);

		Assert.Equal(
			GameplayAbilityTaskCompletionReason.Cancelled,
			change.Reason);

		Assert.Equal(0, runtime.Events.Publish(new GameplayEvent(
			EventSchema.AttackHit)));
		Assert.Equal(
			0,
			recorder.Count(GameplayAbilityTaskChangeKind.Completed));
	}

	[Fact]
	public void EndingTheAbility_CancelsTheWaitBeforeItsNotification()
	{
		using AbilityRuntime runtime = new();
		TaskRecorder recorder = new(runtime.Tasks);
		AbilityRecorder abilities = new(runtime.Abilities);
		Entity actor = runtime.CreateActor();

		GameplayAbilityActivation activation =
			TaskTestSupport.ActivateChannel(
				runtime,
				actor,
				out GameplayAbilityHandle handle);

		GameplayAbilityTaskHandle task = runtime.Tasks.WaitEvent(
			activation,
			EventSchema.AttackHit);

		Assert.True(runtime.Abilities.End(handle));

		GameplayAbilityTaskChange change =
			recorder.Last(GameplayAbilityTaskChangeKind.Cancelled);

		Assert.Equal(task, change.Handle);
		Assert.Equal(
			GameplayAbilityTaskCompletionReason.AbilityEnded,
			change.Reason);
		Assert.Equal(0, runtime.Tasks.PendingTaskCount);
		Assert.Equal("Granted,Activated,Ended", abilities.Sequence());

		Assert.Equal(0, runtime.Events.Publish(new GameplayEvent(
			EventSchema.AttackHit)));
		Assert.Equal(
			0,
			recorder.Count(GameplayAbilityTaskChangeKind.Completed));
	}

	[Fact]
	public void DestroyingTheOwner_ReleasesEveryWaitSubscription()
	{
		using AbilityRuntime runtime = new();
		TaskRecorder recorder = new(runtime.Tasks);
		Entity actor = runtime.CreateActor();

		GameplayAbilityActivation activation =
			TaskTestSupport.ActivateChannel(runtime, actor, out _);

		for (int index = 0; index < 8; index++)
		{
			runtime.Tasks.WaitEvent(
				activation,
				EventSchema.AttackHit);
		}

		Assert.Equal(8, runtime.Tasks.PendingTaskCount);
		Assert.Equal(8, runtime.Events.SubscriptionCount);

		Assert.True(runtime.World.DestroyEntity(actor));

		Assert.Equal(0, runtime.Tasks.PendingTaskCount);
		Assert.Equal(0, runtime.Events.SubscriptionCount);
		Assert.Equal(0, runtime.Events.Publish(new GameplayEvent(
			EventSchema.AttackHit)));
		Assert.Equal(
			0,
			recorder.Count(GameplayAbilityTaskChangeKind.Cancelled));
	}

	[Fact]
	public void Retrigger_CancelsTheOldTaskAndBindsTheNewActivation()
	{
		using AbilityRuntime runtime = new();
		TaskRecorder recorder = new(runtime.Tasks);
		Entity actor = runtime.CreateActor();

		GameplayAbilityHandle handle = runtime.Abilities.GiveAbility(
			actor,
			AbilitySchema.Retriggerable);

		GameplayAbilityActivation first =
			runtime.Abilities.TryActivate(handle).Activation;

		Assert.False(first.IsNone);

		GameplayAbilityTaskHandle oldTask = runtime.Tasks.WaitEvent(
			first,
			EventSchema.AttackHit);

		GameplayAbilityActivationResult retrigger =
			runtime.Abilities.TryActivate(handle);

		Assert.True(retrigger.Succeeded);
		Assert.NotEqual(first, retrigger.Activation);

		Assert.False(runtime.Tasks.TryGetTask(oldTask, out _));
		Assert.Equal(0, runtime.Tasks.PendingTaskCount);

		GameplayAbilityTaskHandle newTask = runtime.Tasks.WaitEvent(
			retrigger.Activation,
			EventSchema.AttackHit);

		Assert.False(newTask.IsNone);

		Assert.Equal(1, runtime.Events.Publish(new GameplayEvent(
			EventSchema.AttackHit)));

		Assert.False(runtime.Tasks.TryGetTask(newTask, out _));
		Assert.Equal(0, runtime.Tasks.PendingTaskCount);

		Assert.Equal(
			1,
			recorder.Changes.Count(change =>
				change.Kind == GameplayAbilityTaskChangeKind.Completed
				&& change.Handle == newTask));
		Assert.Equal(
			1,
			recorder.Changes.Count(change =>
				change.Kind == GameplayAbilityTaskChangeKind.Cancelled
				&& change.Handle == oldTask));
	}
}
