using Volt.Ecs;
using Volt.Gameplay.Abilities;
using Volt.Gameplay.Attributes;
using Volt.Gameplay.Effects;
using Volt.Gameplay.Events;
using Volt.Gameplay.Tags;
using Volt.Gameplay.Tasks;
using Volt.Gameplay.Tests.Abilities;
using Volt.Gameplay.Tests.Events;
using Volt.Gameplay.Tests.Tags;

namespace Volt.Gameplay.Tests.Tasks;

/// <summary>
/// Covers ending, revoking, slot reuse, stale deadlines and
/// subscriptions, cross-system handles, and disposal.
/// </summary>
public sealed class GameplayAbilityTaskLifecycleTests
{
	[Fact]
	public void EndingTheAbility_ReleasesEveryTaskKind()
	{
		using AbilityRuntime runtime = new();
		TaskRecorder recorder = new(runtime.Tasks);
		Entity actor = runtime.CreateActor();

		GameplayAbilityActivation activation =
			TaskTestSupport.ActivateChannel(
				runtime,
				actor,
				out GameplayAbilityHandle handle);

		runtime.Tasks.WaitDuration(activation, 10f);
		runtime.Tasks.WaitEvent(activation, EventSchema.AttackHit);
		runtime.Tasks.WaitTag(
			activation,
			TagSchema.StatusStunned,
			GameplayTagWaitMode.Present);

		Assert.Equal(3, runtime.Tasks.PendingTaskCount);
		Assert.Equal(3, recorder.Count(
			GameplayAbilityTaskChangeKind.Started));

		Assert.True(runtime.Abilities.End(handle));

		Assert.Equal(0, runtime.Tasks.PendingTaskCount);
		Assert.Equal(3, recorder.Count(
			GameplayAbilityTaskChangeKind.Cancelled));
		Assert.Equal(0, runtime.Events.SubscriptionCount);

		runtime.Tasks.Advance(100f);

		Assert.Equal(0, recorder.Count(
			GameplayAbilityTaskChangeKind.Completed));
	}

	[Fact]
	public void RevokingTheGrant_ReleasesItsTasks()
	{
		using AbilityRuntime runtime = new();
		TaskRecorder recorder = new(runtime.Tasks);
		Entity actor = runtime.CreateActor();

		GameplayAbilityActivation activation =
			TaskTestSupport.ActivateChannel(
				runtime,
				actor,
				out GameplayAbilityHandle handle);

		runtime.Tasks.WaitEvent(activation, EventSchema.AttackHit);

		Assert.True(runtime.Abilities.RevokeAbility(handle));

		Assert.Equal(0, runtime.Tasks.PendingTaskCount);
		Assert.Equal(1, recorder.Count(
			GameplayAbilityTaskChangeKind.Cancelled));
		Assert.Equal(
			GameplayAbilityTaskCompletionReason.AbilityEnded,
			recorder.Last(GameplayAbilityTaskChangeKind.Cancelled).Reason);
		Assert.Equal(0, runtime.Events.Publish(new GameplayEvent(
			EventSchema.AttackHit)));
	}

	[Fact]
	public void CancelAll_LeavesTasksStartedByTheCancellationAlone()
	{
		using AbilityRuntime runtime = new();
		Entity actor = runtime.CreateActor();

		GameplayAbilityActivation activation =
			TaskTestSupport.ActivateChannel(runtime, actor, out _);

		GameplayAbilityTaskHandle first =
			runtime.Tasks.WaitDuration(activation, 10f);

		GameplayAbilityTaskHandle added =
			GameplayAbilityTaskHandle.None;

		runtime.Tasks.Changed += change =>
		{
			if (change.Kind == GameplayAbilityTaskChangeKind.Cancelled
				&& change.Handle == first
				&& added.IsNone)
			{
				added = runtime.Tasks.WaitDuration(activation, 10f);
			}
		};

		Assert.Equal(1, runtime.Tasks.CancelAll(actor));

		Assert.False(added.IsNone);
		Assert.Equal(1, runtime.Tasks.PendingTaskCount);
		Assert.True(runtime.Tasks.TryGetTask(added, out _));

		Assert.True(runtime.Tasks.Cancel(added));
		Assert.Equal(0, runtime.Tasks.PendingTaskCount);
	}

	[Fact]
	public void SlotReuse_DoesNotResolveOldTaskHandles()
	{
		using AbilityRuntime runtime = new();
		Entity actor = runtime.CreateActor();

		GameplayAbilityActivation activation =
			TaskTestSupport.ActivateChannel(runtime, actor, out _);

		GameplayAbilityTaskHandle[] stale =
			new GameplayAbilityTaskHandle[128];

		for (int index = 0; index < stale.Length; index++)
		{
			stale[index] = runtime.Tasks.WaitDuration(activation, 10f);

			Assert.True(runtime.Tasks.Cancel(stale[index]));
		}

		GameplayAbilityTaskHandle replacement =
			runtime.Tasks.WaitDuration(activation, 10f);

		Assert.Equal(stale[^1].Slot, replacement.Slot);
		Assert.NotEqual(stale[^1], replacement);

		foreach (GameplayAbilityTaskHandle handle in stale)
		{
			Assert.False(runtime.Tasks.Cancel(handle));
			Assert.False(runtime.Tasks.Complete(handle));
			Assert.False(runtime.Tasks.TryGetTask(handle, out _));
		}

		Assert.True(runtime.Tasks.TryGetTask(replacement, out _));
		Assert.Equal(1, runtime.Tasks.PendingTaskCount);
	}

	[Fact]
	public void StaleDeadline_IsDiscardedWithoutWakingARecycledSlot()
	{
		using AbilityRuntime runtime = new();
		TaskRecorder recorder = new(runtime.Tasks);
		Entity actor = runtime.CreateActor();

		GameplayAbilityActivation activation =
			TaskTestSupport.ActivateChannel(runtime, actor, out _);

		GameplayAbilityTaskHandle cancelled =
			runtime.Tasks.WaitDuration(activation, 1f);

		Assert.True(runtime.Tasks.Cancel(cancelled));

		GameplayAbilityTaskHandle replacement =
			runtime.Tasks.WaitDuration(activation, 100f);

		Assert.Equal(cancelled.Slot, replacement.Slot);

		runtime.Tasks.Advance(50f);

		Assert.Equal(
			0,
			recorder.Count(GameplayAbilityTaskChangeKind.Completed));
		Assert.True(runtime.Tasks.TryGetTask(replacement, out _));
		Assert.Equal(1, runtime.Tasks.PendingTaskCount);
	}

	[Fact]
	public void StaleEventSubscription_DoesNotCompleteAReusedSlot()
	{
		using AbilityRuntime runtime = new();
		TaskRecorder recorder = new(runtime.Tasks);
		Entity actor = runtime.CreateActor();

		GameplayAbilityActivation activation =
			TaskTestSupport.ActivateChannel(runtime, actor, out _);

		GameplayAbilityTaskHandle first =
			runtime.Tasks.WaitEvent(activation, EventSchema.AttackHit);

		Assert.True(runtime.Tasks.Cancel(first));

		GameplayAbilityTaskHandle replacement =
			runtime.Tasks.WaitEvent(activation, EventSchema.AttackHit);

		Assert.Equal(first.Slot, replacement.Slot);

		Assert.Equal(1, runtime.Events.Publish(new GameplayEvent(
			EventSchema.AttackHit)));

		Assert.Equal(
			1,
			recorder.Count(GameplayAbilityTaskChangeKind.Completed));
		Assert.Equal(
			replacement,
			recorder.Last(GameplayAbilityTaskChangeKind.Completed).Handle);
		Assert.False(runtime.Tasks.TryGetTask(first, out _));
	}

	[Fact]
	public void TaskHandlesFromAnotherSystem_AreRejected()
	{
		using AbilityRuntime runtime = new();
		Entity actor = runtime.CreateActor();

		using GameplayAbilitySystem other = new(
			runtime.World,
			runtime.Tags,
			runtime.Effects,
			runtime.Attributes,
			runtime.Events);

		GameplayAbilityActivation activation =
			TaskTestSupport.ActivateChannel(runtime, actor, out _);

		GameplayAbilityTaskHandle task =
			runtime.Tasks.WaitDuration(activation, 10f);

		Assert.False(other.Tasks.Cancel(task));
		Assert.False(other.Tasks.Complete(task));
		Assert.False(other.Tasks.TryGetTask(task, out _));
		Assert.True(other.Tasks.WaitDuration(activation, 10f).IsNone);
		Assert.True(other.Tasks.WaitEvent(
			activation,
			EventSchema.AttackHit).IsNone);
		Assert.True(other.Tasks.WaitTag(
			activation,
			TagSchema.StatusStunned,
			GameplayTagWaitMode.Present).IsNone);
		Assert.True(runtime.Tasks.TryGetTask(task, out _));
	}

	[Fact]
	public void ExternalEventDisposal_DoesNotBreakTaskCleanup()
	{
		using AbilityRuntime runtime = new();
		Entity actor = runtime.CreateActor();

		GameplayAbilityActivation activation =
			TaskTestSupport.ActivateChannel(
				runtime,
				actor,
				out GameplayAbilityHandle handle);

		runtime.Tasks.WaitEvent(activation, EventSchema.AttackHit);

		Assert.Equal(1, runtime.Events.SubscriptionCount);

		runtime.Events.Dispose();

		// The task still exists and its handle still resolves, but the
		// router can no longer deliver. Cancelling the activation must
		// not throw ObjectDisposedException from subscription cleanup.
		Assert.Equal(1, runtime.Tasks.PendingTaskCount);
		Assert.True(runtime.Abilities.End(handle));
		Assert.Equal(0, runtime.Tasks.PendingTaskCount);

		runtime.Tasks.Dispose();
	}

	[Fact]
	public void Dispose_ReleasesTasksAndEventSubscriptions()
	{
		using World world = new();
		using GameplayAttributeStore attributes = new(world);
		using GameplayTagStore tags = new(world);
		using GameplayEffectSystem effects = new(world, attributes, tags);
		using GameplayEventSystem events = new(world);

		GameplayAbilitySystem abilities = new(
			world,
			tags,
			effects,
			attributes,
			events);

		GameplayAbilityTaskSystem tasks = abilities.Tasks;
		Entity actor = world.CreateEntity();
		GameplayAbilityHandle handle = abilities.GiveAbility(
			actor,
			AbilitySchema.Channel);

		GameplayAbilityActivation activation =
			abilities.TryActivate(handle).Activation;

		tasks.WaitEvent(activation, EventSchema.AttackHit);
		tasks.WaitDuration(activation, 10f);

		Assert.Equal(2, tasks.PendingTaskCount);
		Assert.Equal(1, events.SubscriptionCount);

		tasks.Dispose();

		Assert.Equal(0, events.SubscriptionCount);
		Assert.Throws<ObjectDisposedException>(
			() => tasks.Advance(1f));
		Assert.Throws<ObjectDisposedException>(
			() => tasks.TryGetTask(
				default,
				out _));

		abilities.Dispose();
	}
}
