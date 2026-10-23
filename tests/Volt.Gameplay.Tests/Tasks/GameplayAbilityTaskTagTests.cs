using Volt.Ecs;
using Volt.Gameplay.Abilities;
using Volt.Gameplay.Events;
using Volt.Gameplay.Tags;
using Volt.Gameplay.Tasks;
using Volt.Gameplay.Tests.Abilities;
using Volt.Gameplay.Tests.Events;
using Volt.Gameplay.Tests.Tags;

namespace Volt.Gameplay.Tests.Tasks;

/// <summary>
/// Covers tag-condition tasks, which are armed by tag changes instead
/// of polled every frame.
/// </summary>
public sealed class GameplayAbilityTaskTagTests
{
	[Fact]
	public void WaitTagPresent_CompletesAfterTheTagArrives()
	{
		using AbilityRuntime runtime = new();
		TaskRecorder recorder = new(runtime.Tasks);
		Entity actor = runtime.CreateActor();

		GameplayAbilityActivation activation =
			TaskTestSupport.ActivateChannel(runtime, actor, out _);

		GameplayAbilityTaskHandle task = runtime.Tasks.WaitTag(
			activation,
			TagSchema.StatusStunned,
			GameplayTagWaitMode.Present);

		Assert.False(task.IsNone);

		// No polling: time alone does not complete the task.
		runtime.Tasks.Advance(5f);

		Assert.Equal(1, runtime.Tasks.PendingTaskCount);
		Assert.Equal(
			0,
			recorder.Count(GameplayAbilityTaskChangeKind.Completed));

		Assert.True(runtime.Tags.AddTag(actor, TagSchema.StatusStunned));

		runtime.Tasks.Advance(0.016f);

		Assert.Equal(0, runtime.Tasks.PendingTaskCount);
		Assert.Equal(
			1,
			recorder.Count(GameplayAbilityTaskChangeKind.Completed));
		Assert.Equal(
			GameplayAbilityTaskCompletionReason.ConditionMet,
			recorder.Last(GameplayAbilityTaskChangeKind.Completed).Reason);
	}

	[Fact]
	public void WaitTagPresent_AlreadySatisfied_CompletesOnTheNextAdvance()
	{
		using AbilityRuntime runtime = new();
		TaskRecorder recorder = new(runtime.Tasks);
		Entity actor = runtime.CreateActor();

		Assert.True(runtime.Tags.AddTag(actor, TagSchema.StatusStunned));

		GameplayAbilityActivation activation =
			TaskTestSupport.ActivateChannel(runtime, actor, out _);

		GameplayAbilityTaskHandle task = runtime.Tasks.WaitTag(
			activation,
			TagSchema.StatusStunned,
			GameplayTagWaitMode.Present);

		Assert.False(task.IsNone);
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
	public void WaitTagPresent_ConditionThatFlipsBack_KeepsWaiting()
	{
		using AbilityRuntime runtime = new();
		TaskRecorder recorder = new(runtime.Tasks);
		Entity actor = runtime.CreateActor();

		GameplayAbilityActivation activation =
			TaskTestSupport.ActivateChannel(runtime, actor, out _);

		runtime.Tasks.WaitTag(
			activation,
			TagSchema.StatusStunned,
			GameplayTagWaitMode.Present);

		Assert.True(runtime.Tags.AddTag(actor, TagSchema.StatusStunned));
		Assert.True(runtime.Tags.RemoveTag(actor, TagSchema.StatusStunned));

		runtime.Tasks.Advance(0.016f);

		Assert.Equal(1, runtime.Tasks.PendingTaskCount);
		Assert.Equal(
			0,
			recorder.Count(GameplayAbilityTaskChangeKind.Completed));

		Assert.True(runtime.Tags.AddTag(actor, TagSchema.StatusStunned));

		runtime.Tasks.Advance(0.016f);

		Assert.Equal(0, runtime.Tasks.PendingTaskCount);
		Assert.Equal(
			1,
			recorder.Count(GameplayAbilityTaskChangeKind.Completed));
	}

	[Fact]
	public void WaitTagAbsent_CompletesWhenTheLastGrantGoes()
	{
		using AbilityRuntime runtime = new();
		TaskRecorder recorder = new(runtime.Tasks);
		Entity actor = runtime.CreateActor();

		Assert.True(runtime.Tags.AddTag(actor, TagSchema.StatusStunned));
		Assert.True(runtime.Tags.AddTag(actor, TagSchema.StatusStunned));

		GameplayAbilityActivation activation =
			TaskTestSupport.ActivateChannel(runtime, actor, out _);

		runtime.Tasks.WaitTag(
			activation,
			TagSchema.StatusStunned,
			GameplayTagWaitMode.Absent);

		runtime.Tasks.Advance(0.016f);

		Assert.Equal(1, runtime.Tasks.PendingTaskCount);

		Assert.True(runtime.Tags.RemoveTag(actor, TagSchema.StatusStunned));

		runtime.Tasks.Advance(0.016f);

		// One grant remains, so absence does not hold yet.
		Assert.Equal(1, runtime.Tasks.PendingTaskCount);

		Assert.True(runtime.Tags.RemoveTag(actor, TagSchema.StatusStunned));

		runtime.Tasks.Advance(0.016f);

		Assert.Equal(0, runtime.Tasks.PendingTaskCount);
		Assert.Equal(
			1,
			recorder.Count(GameplayAbilityTaskChangeKind.Completed));
	}

	[Fact]
	public void WaitTag_ExactAndHierarchicalDiffer()
	{
		using AbilityRuntime runtime = new();
		TaskRecorder recorder = new(runtime.Tasks);
		Entity actor = runtime.CreateActor();

		GameplayAbilityActivation activation =
			TaskTestSupport.ActivateChannel(runtime, actor, out _);

		GameplayAbilityTaskHandle exact = runtime.Tasks.WaitTag(
			activation,
			TagSchema.Status,
			GameplayTagWaitMode.Present,
			exact: true);

		GameplayAbilityTaskHandle hierarchical = runtime.Tasks.WaitTag(
			activation,
			TagSchema.Status,
			GameplayTagWaitMode.Present);

		Assert.True(runtime.Tags.AddTag(actor, TagSchema.StatusStunned));

		runtime.Tasks.Advance(0.016f);

		Assert.False(runtime.Tasks.TryGetTask(hierarchical, out _));
		Assert.True(runtime.Tasks.TryGetTask(exact, out _));

		Assert.True(runtime.Tags.AddTag(actor, TagSchema.Status));

		runtime.Tasks.Advance(0.016f);

		Assert.False(runtime.Tasks.TryGetTask(exact, out _));
		Assert.Equal(0, runtime.Tasks.PendingTaskCount);
		Assert.Equal(
			2,
			recorder.Count(GameplayAbilityTaskChangeKind.Completed));
	}

	[Fact]
	public void WaitTag_IgnoresTagChangesOnOtherEntities()
	{
		using AbilityRuntime runtime = new();
		TaskRecorder recorder = new(runtime.Tasks);
		Entity actor = runtime.CreateActor();
		Entity other = runtime.CreateActor();

		GameplayAbilityActivation activation =
			TaskTestSupport.ActivateChannel(runtime, actor, out _);

		runtime.Tasks.WaitTag(
			activation,
			TagSchema.StatusStunned,
			GameplayTagWaitMode.Present);

		Assert.True(runtime.Tags.AddTag(other, TagSchema.StatusStunned));

		runtime.Tasks.Advance(0.016f);

		Assert.Equal(1, runtime.Tasks.PendingTaskCount);
		Assert.Equal(
			0,
			recorder.Count(GameplayAbilityTaskChangeKind.Completed));
	}

	[Fact]
	public void EndingTheAbility_StopsTheTagWait()
	{
		using AbilityRuntime runtime = new();
		TaskRecorder recorder = new(runtime.Tasks);
		Entity actor = runtime.CreateActor();

		GameplayAbilityActivation activation =
			TaskTestSupport.ActivateChannel(
				runtime,
				actor,
				out GameplayAbilityHandle handle);

		runtime.Tasks.WaitTag(
			activation,
			TagSchema.StatusStunned,
			GameplayTagWaitMode.Present);

		Assert.True(runtime.Abilities.End(handle));

		Assert.Equal(
			1,
			recorder.Count(GameplayAbilityTaskChangeKind.Cancelled));

		Assert.True(runtime.Tags.AddTag(actor, TagSchema.StatusStunned));

		runtime.Tasks.Advance(1f);

		Assert.Equal(
			0,
			recorder.Count(GameplayAbilityTaskChangeKind.Completed));
		Assert.Equal(0, runtime.Tasks.PendingTaskCount);
	}

	[Fact]
	public void WaitTag_WithOtherTaskKindsPending_CompletesOnlyTheTagTask()
	{
		using AbilityRuntime runtime = new();
		Entity actor = runtime.CreateActor();

		GameplayAbilityActivation activation =
			TaskTestSupport.ActivateChannel(runtime, actor, out _);

		GameplayAbilityTaskHandle duration =
			runtime.Tasks.WaitDuration(activation, 10f);

		GameplayAbilityTaskHandle eventTask =
			runtime.Tasks.WaitEvent(activation, EventSchema.AttackHit);

		GameplayAbilityTaskHandle tagTask = runtime.Tasks.WaitTag(
			activation,
			TagSchema.StatusStunned,
			GameplayTagWaitMode.Present);

		Assert.Equal(3, runtime.Tasks.PendingTaskCount);

		Assert.True(runtime.Tags.AddTag(actor, TagSchema.StatusStunned));

		runtime.Tasks.Advance(0.001f);

		Assert.False(runtime.Tasks.TryGetTask(tagTask, out _));
		Assert.True(runtime.Tasks.TryGetTask(duration, out _));
		Assert.True(runtime.Tasks.TryGetTask(eventTask, out _));
		Assert.Equal(2, runtime.Tasks.PendingTaskCount);

		runtime.Tasks.Advance(10f);

		Assert.False(runtime.Tasks.TryGetTask(duration, out _));
		Assert.True(runtime.Tasks.TryGetTask(eventTask, out _));
		Assert.Equal(1, runtime.Tasks.PendingTaskCount);

		runtime.Events.Publish(new GameplayEvent(EventSchema.AttackHit));

		Assert.False(runtime.Tasks.TryGetTask(eventTask, out _));
		Assert.Equal(0, runtime.Tasks.PendingTaskCount);
	}

	[Fact]
	public void WaitTag_InvalidArguments_Throw()
	{
		using AbilityRuntime runtime = new();
		Entity actor = runtime.CreateActor();

		GameplayAbilityActivation activation =
			TaskTestSupport.ActivateChannel(runtime, actor, out _);

		Assert.Throws<ArgumentException>(() => runtime.Tasks.WaitTag(
			activation,
			GameplayTag.None,
			GameplayTagWaitMode.Present));

		Assert.Throws<ArgumentOutOfRangeException>(
			() => runtime.Tasks.WaitTag(
				activation,
				TagSchema.StatusStunned,
				(GameplayTagWaitMode)99));

		Assert.Equal(0, runtime.Tasks.PendingTaskCount);
	}
}
