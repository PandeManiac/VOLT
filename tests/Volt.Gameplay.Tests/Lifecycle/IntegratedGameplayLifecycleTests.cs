using Volt.Ecs;
using Volt.Gameplay.Abilities;
using Volt.Gameplay.Cues;
using Volt.Gameplay.Effects;
using Volt.Gameplay.Tags;
using Volt.Gameplay.Tasks;
using Volt.Gameplay.Tests.Abilities;
using Volt.Gameplay.Tests.Tags;
using Volt.Gameplay.Tests.Tasks;

namespace Volt.Gameplay.Tests.Lifecycle;

/// <summary>
/// Adversarial compositions that cross the store boundaries at once:
/// an effect that grants a tag and owns a cue, an ability that reacts
/// to the tag, a task waiting on it, and an entity that disappears in
/// the middle of the notification that created it.
/// </summary>
/// <remarks>
/// Each test states the expected outcome through the public contracts
/// only. The point is not to cover one subsystem again, but to prove
/// that removing the entity underneath a committed transition leaves
/// no stale handle that the recycled index can act through.
/// </remarks>
public sealed class IntegratedGameplayLifecycleTests
{
	private static readonly GameplayEffectDefinition CueAndTag =
		GameplayEffectDefinition
			.Create("Test.Integrated.CueAndTag")
			.Infinite()
			.GrantTags(TagSchema.StatusBurning)
			.GameplayCue(TagSchema.CueBurning)
			.Build();

	[Fact]
	public void TargetDestroyedByItsOwnCue_LeavesNothingForTheRecycledIndex()
	{
		using AbilityRuntime runtime = new();
		using GameplayCueSystem cues = new(runtime.World);
		using GameplayCueBuffer buffer = new(8, 32);

		cues.BindEffects(runtime.Effects);
		cues.PresentationBuffer = buffer;

		Entity victim = runtime.CreateActor();
		GameplayEffectHandle effect = default;
		bool destroyed = false;

		cues.Subscribe(TagSchema.CueBurning, change =>
		{
			if (destroyed || change.Kind != GameplayCueKind.Added)
			{
				return;
			}

			destroyed = true;

			// The presentation handoff is still queued, but gameplay
			// cleanup must remove the instance and its cue.
			Assert.True(runtime.World.DestroyEntity(change.Target));
		});

		effect = runtime.Effects.Apply(victim, CueAndTag).Handle;

		Assert.True(destroyed);
		Assert.Equal(0, runtime.Effects.ActiveEffectCount);
		Assert.Equal(0, cues.ActiveCueCount);
		Assert.Equal(0, runtime.Tags.TaggedEntityCount);
		Assert.False(runtime.Effects.Remove(effect));
		Assert.False(
			runtime.World.IsAlive(victim),
			$"victim {victim} still alive, count {runtime.World.Count}");
		Assert.Throws<ArgumentException>(
			() => cues.Add(TagSchema.CueBurning, target: victim));

		Entity replacement = runtime.CreateActor();

		Assert.Equal(victim.Index, replacement.Index);
		Assert.NotEqual(victim.Generation, replacement.Generation);

		// Neither a stale effect handle nor a stale cue may name the
		// replacement, and the replacement starts clean.
		Assert.False(runtime.Effects.Remove(effect));
		Assert.Equal(0, runtime.Effects.GetEffectCount(replacement));
		Assert.Equal(0, runtime.Tags.GetTagCount(
			replacement,
			TagSchema.StatusBurning));
		Assert.Equal(0, cues.ActiveCueCount);

		// The removal transition is still observable to the consumer.
		Span<GameplayCueEvent> events = stackalloc GameplayCueEvent[8];
		int drained = buffer.Drain(events);
		List<GameplayCueKind> kinds = [];

		for (int index = 0; index < drained; index++)
		{
			kinds.Add(events[index].Kind);
		}

		Assert.Equal(
			[GameplayCueKind.Added, GameplayCueKind.Removed],
			kinds);
	}

	[Fact]
	public void CancellingActivationDestroyedByItsCue_ReleasesTasksAndGrants()
	{
		using AbilityRuntime runtime = new();
		using GameplayCueSystem cues = new(runtime.World);
		Entity actor = runtime.CreateActor();

		cues.BindEffects(runtime.Effects);

		TaskRecorder tasks = new(runtime.Tasks);

		GameplayAbilityActivation activation =
			TaskTestSupport.ActivateChannel(runtime, actor, out _);

		GameplayAbilityTaskHandle wait = runtime.Tasks.WaitEvent(
			activation,
			TagSchema.CueBurning);

		Assert.False(wait.IsNone);
		Assert.Equal(1, runtime.Tasks.PendingTaskCount);

		// The cue fires while the task is pending, and the listener
		// ends the activation. The task must be cancelled, not left
		// pending on a dead activation.
		cues.Subscribe(TagSchema.CueBurning, change =>
		{
			if (change.Kind == GameplayCueKind.Added)
			{
				runtime.Abilities.CancelAll(actor);
			}
		});

		runtime.Effects.Apply(actor, CueAndTag);

		Assert.Equal(0, runtime.Tasks.PendingTaskCount);
		Assert.Equal(0, runtime.Abilities.ActiveAbilityCount);
		Assert.Equal(
			1,
			tasks.Count(GameplayAbilityTaskChangeKind.Cancelled));

		// The effect that fired the cue is still a committed instance
		// and can be removed exactly once.
		Assert.Equal(1, runtime.Effects.ActiveEffectCount);
		Assert.Equal(1, runtime.Effects.RemoveAll(actor));
		Assert.Equal(0, runtime.Effects.ActiveEffectCount);
		Assert.Equal(0, cues.ActiveCueCount);
		Assert.Equal(0, runtime.Tags.TaggedEntityCount);
	}

	[Fact]
	public void ThrowingCueListener_DoesNotSkipEffectCleanup()
	{
		using AbilityRuntime runtime = new();
		using GameplayCueSystem cues = new(runtime.World);
		Entity actor = runtime.CreateActor();

		cues.BindEffects(runtime.Effects);

		int delivered = 0;

		cues.Subscribe(TagSchema.CueBurning, change =>
		{
			delivered++;

			if (change.Kind == GameplayCueKind.Removed)
			{
				throw new InvalidOperationException("presentation failure");
			}
		});

		GameplayEffectHandle handle = runtime.Effects.Apply(actor, CueAndTag).Handle;

		Assert.Throws<InvalidOperationException>(
			() => runtime.Effects.Remove(handle));

		Assert.Equal(2, delivered);
		Assert.Equal(0, runtime.Effects.ActiveEffectCount);
		Assert.Equal(0, runtime.Tags.GetTagCount(
			actor,
			TagSchema.StatusBurning));
		Assert.Equal(0, cues.ActiveCueCount);
	}
}
