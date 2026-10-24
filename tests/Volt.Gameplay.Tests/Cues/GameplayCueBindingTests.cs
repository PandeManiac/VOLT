using Volt.Ecs;
using Volt.Gameplay.Cues;
using Volt.Gameplay.Effects;
using Volt.Gameplay.Tags;
using Volt.Gameplay.Tests.Abilities;
using Volt.Gameplay.Tests.Tags;

namespace Volt.Gameplay.Tests.Cues;

/// <summary>
/// Covers the binding contract between the cue system and the effect
/// system, including effects that exist before the binding.
/// </summary>
/// <remarks>
/// The cue system cannot observe transitions that happened before it
/// subscribed, so binding an effect system that already has active
/// instances has to adopt their current state explicitly. These tests
/// pin that adoption: one cue per uninhibited instance, no cue for an
/// inhibited or instant instance, and no duplicate when the bind is
/// repeated.
/// </remarks>
public sealed class GameplayCueBindingTests
{
	private static readonly GameplayEffectDefinition PersistentCue =
		GameplayEffectDefinition
			.Create("Test.CueBinding.Persistent")
			.Infinite()
			.GrantTags(TagSchema.StatusStunned)
			.GameplayCue(TagSchema.CueBurning)
			.Build();

	private static readonly GameplayEffectDefinition StackingCue =
		GameplayEffectDefinition
			.Create("Test.CueBinding.Stacking")
			.HasDuration(10f)
			.Stacking(GameplayEffectStackingType.AggregateByTarget, 3)
			.GameplayCue(TagSchema.CueBurning)
			.Build();

	private static readonly GameplayEffectDefinition InhibitedCue =
		GameplayEffectDefinition
			.Create("Test.CueBinding.Inhibited")
			.Infinite()
			.GameplayCue(TagSchema.CueBurning)
			.RequireWhileActive(
				GameplayTagRequirements.Require(
					GameplayTagSet.Create(TagSchema.StatusBurning)))
			.Build();

	[Fact]
	public void BindBeforeEffects_RoutesTheLiveTransition()
	{
		using AbilityRuntime runtime = new();
		using GameplayCueSystem cues = new(runtime.World);
		List<GameplayCueKind> kinds = [];

		cues.BindEffects(runtime.Effects);
		cues.Subscribe(TagSchema.CueBurning, change => kinds.Add(change.Kind));

		Entity entity = runtime.CreateActor();

		runtime.Effects.Apply(entity, PersistentCue);

		Assert.Equal(1, cues.ActiveCueCount);
		Assert.Equal([GameplayCueKind.Added], kinds);
	}

	[Fact]
	public void BindAfterEffects_AdoptsEveryInstanceThatShouldHaveACue()
	{
		using AbilityRuntime runtime = new();
		Entity first = runtime.CreateActor();
		Entity second = runtime.CreateActor();

		runtime.Effects.Apply(first, PersistentCue);
		runtime.Effects.Apply(second, PersistentCue);

		using GameplayCueSystem cues = new(runtime.World);
		List<GameplayCueEvent> events = [];

		cues.Changed += events.Add;
		cues.BindEffects(runtime.Effects);

		Assert.Equal(2, cues.ActiveCueCount);
		Assert.Equal(2, events.Count);
		Assert.All(events, cueEvent => Assert.Equal(GameplayCueKind.Added, cueEvent.Kind));
		Assert.Equal(
			[first, second],
			events.Select(cueEvent => cueEvent.Target));

		// The adopted cues are the live ones: ending an instance now
		// removes exactly that cue.
		runtime.Effects.RemoveAll(first);

		Assert.Equal(1, cues.ActiveCueCount);
		Assert.Equal(
			GameplayCueKind.Removed,
			events[^1].Kind);
		Assert.Equal(first, events[^1].Target);
	}

	[Fact]
	public void BindAfterStackedEffect_AdoptsTheCurrentStackCount()
	{
		using AbilityRuntime runtime = new();
		Entity entity = runtime.CreateActor();

		runtime.Effects.Apply(entity, StackingCue);
		GameplayEffectApplyResult stacked = runtime.Effects.Apply(entity, StackingCue);

		Assert.Equal(2, stacked.StackCount);

		using GameplayCueSystem cues = new(runtime.World);
		GameplayCueEvent added = default;

		cues.Changed += cueEvent => added = cueEvent;
		cues.BindEffects(runtime.Effects);

		Assert.Equal(1, cues.ActiveCueCount);
		Assert.Equal(2, added.StackCount);
		Assert.Equal(2f, added.Magnitude);
		Assert.Equal(entity, added.Target);
	}

	[Fact]
	public void ApplyingAnInhibitedEffect_CreatesNoCue()
	{
		using AbilityRuntime runtime = new();
		using GameplayCueSystem cues = new(runtime.World);
		Entity entity = runtime.CreateActor();

		cues.BindEffects(runtime.Effects);

		GameplayEffectApplyResult result = runtime.Effects.Apply(
			entity,
			InhibitedCue);

		Assert.Equal(GameplayEffectApplyStatus.Applied, result.Status);
		Assert.Equal(0, cues.ActiveCueCount);
		Assert.True(runtime.Effects.TryFindEffect(
			entity,
			InhibitedCue,
			out ActiveGameplayEffect active));
		Assert.True(active.IsInhibited);
	}

	[Fact]
	public void BindAfterInhibitedEffect_CreatesNoCueUntilItResumes()
	{
		using AbilityRuntime runtime = new();
		Entity entity = runtime.CreateActor();

		runtime.Effects.Apply(entity, InhibitedCue);

		using GameplayCueSystem cues = new(runtime.World);
		List<GameplayCueKind> kinds = [];

		cues.Changed += cueEvent => kinds.Add(cueEvent.Kind);
		cues.BindEffects(runtime.Effects);

		Assert.Equal(0, cues.ActiveCueCount);
		Assert.Empty(kinds);

		Assert.True(runtime.Tags.AddTag(entity, TagSchema.StatusBurning));

		Assert.Equal(1, cues.ActiveCueCount);
		Assert.Equal([GameplayCueKind.Added], kinds);

		Assert.True(runtime.Tags.RemoveTag(entity, TagSchema.StatusBurning));

		Assert.Equal(0, cues.ActiveCueCount);
		Assert.Equal(
			[GameplayCueKind.Added, GameplayCueKind.Removed],
			kinds);
	}

	[Fact]
	public void BindAfterInstantEffect_DoesNotReplayTheOneShot()
	{
		using AbilityRuntime runtime = new();
		Entity entity = runtime.CreateActor();

		GameplayEffectDefinition instant = GameplayEffectDefinition
			.Create("Test.CueBinding.Instant")
			.Instant()
			.GameplayCue(TagSchema.CueBurning)
			.Build();

		runtime.Effects.Apply(entity, instant);

		using GameplayCueSystem cues = new(runtime.World);
		int delivered = 0;

		cues.Changed += _ => delivered++;
		cues.BindEffects(runtime.Effects);

		Assert.Equal(0, cues.ActiveCueCount);
		Assert.Equal(0, delivered);
	}

	[Fact]
	public void BindThenExpiry_RemovesTheAdoptedCue()
	{
		using AbilityRuntime runtime = new();
		Entity entity = runtime.CreateActor();

		runtime.Effects.Apply(entity, StackingCue);

		using GameplayCueSystem cues = new(runtime.World);
		cues.BindEffects(runtime.Effects);

		Assert.Equal(1, cues.ActiveCueCount);

		runtime.Effects.Advance(5f);

		Assert.Equal(1, cues.ActiveCueCount);

		runtime.Effects.Advance(6f);

		Assert.Equal(0, cues.ActiveCueCount);
		Assert.Equal(0, runtime.Effects.ActiveEffectCount);
	}

	[Fact]
	public void Bind_ListenerThatRemovesTheInstanceDuringSynchronization_LeavesNoCue()
	{
		using AbilityRuntime runtime = new();
		Entity entity = runtime.CreateActor();

		runtime.Effects.Apply(entity, PersistentCue);

		using GameplayCueSystem cues = new(runtime.World);
		int removed = 0;

		cues.Changed += cueEvent =>
		{
			if (cueEvent.Kind == GameplayCueKind.Added)
			{
				runtime.Effects.RemoveAll(cueEvent.Target);
			}
			else if (cueEvent.Kind == GameplayCueKind.Removed)
			{
				removed++;
			}
		};

		cues.BindEffects(runtime.Effects);

		Assert.Equal(0, runtime.Effects.ActiveEffectCount);
		Assert.Equal(0, cues.ActiveCueCount);
		Assert.Equal(1, removed);
	}

	[Fact]
	public void RebindingTheSameSystem_IsIdempotent()
	{
		using AbilityRuntime runtime = new();
		Entity entity = runtime.CreateActor();

		runtime.Effects.Apply(entity, PersistentCue);

		using GameplayCueSystem cues = new(runtime.World);
		int delivered = 0;

		cues.Changed += _ => delivered++;
		cues.BindEffects(runtime.Effects);
		cues.BindEffects(runtime.Effects);

		Assert.Equal(1, cues.ActiveCueCount);
		Assert.Equal(1, delivered);

		runtime.Effects.Apply(runtime.CreateActor(), PersistentCue);

		Assert.Equal(2, cues.ActiveCueCount);
	}

	[Fact]
	public void BindingASecondSystem_ThrowsAndLeavesTheFirstBindingWorking()
	{
		using AbilityRuntime runtime = new();
		using GameplayCueSystem cues = new(runtime.World);
		using World otherWorld = new();
		using Volt.Gameplay.Attributes.GameplayAttributeStore otherAttributes =
			new(otherWorld);
		using GameplayTagStore otherTags = new(otherWorld);
		using GameplayEffectSystem otherEffects = new(
			otherWorld,
			otherAttributes,
			otherTags);
		using Volt.Gameplay.Attributes.GameplayAttributeStore secondAttributes =
			new(runtime.World);
		using GameplayTagStore secondTags = new(runtime.World);
		using GameplayEffectSystem secondEffects = new(
			runtime.World,
			secondAttributes,
			secondTags);
		Entity entity = runtime.CreateActor();

		cues.BindEffects(runtime.Effects);

		// A different instance over the same world is rejected by
		// identity, and a system over another world by the world check.
		Assert.Throws<InvalidOperationException>(
			() => cues.BindEffects(secondEffects));
		Assert.Throws<ArgumentException>(
			() => cues.BindEffects(otherEffects));

		runtime.Effects.Apply(entity, PersistentCue);

		Assert.Equal(1, cues.ActiveCueCount);
	}

	[Fact]
	public void ThrowingListenerDuringSynchronization_KeepsTheBindingAndAggregates()
	{
		using AbilityRuntime runtime = new();
		Entity first = runtime.CreateActor();
		Entity second = runtime.CreateActor();

		runtime.Effects.Apply(first, PersistentCue);
		runtime.Effects.Apply(second, PersistentCue);

		using GameplayCueSystem cues = new(runtime.World);
		int delivered = 0;

		cues.Changed += _ =>
		{
			delivered++;

			if (delivered == 1)
			{
				throw new InvalidOperationException("listener failure");
			}
		};

		Assert.Throws<AggregateException>(() => cues.BindEffects(runtime.Effects));

		// The failing cue was rolled back by its own notification
		// contract; the binding itself stayed in place and the second
		// instance was still adopted.
		Assert.Equal(1, cues.ActiveCueCount);

		// Re-binding re-runs the synchronization, adopts the instance
		// that lost its cue, and cannot duplicate the other one.
		cues.BindEffects(runtime.Effects);

		Assert.Equal(2, cues.ActiveCueCount);

		int afterRetry = delivered;

		cues.BindEffects(runtime.Effects);

		Assert.Equal(afterRetry, delivered);
		Assert.Equal(2, cues.ActiveCueCount);

		runtime.Effects.RemoveAll(first);

		Assert.Equal(1, cues.ActiveCueCount);
	}

	[Fact]
	public void Dispose_UnsubscribesFromTheEffectSystem()
	{
		using AbilityRuntime runtime = new();
		using GameplayCueSystem cues = new(runtime.World);
		Entity entity = runtime.CreateActor();

		runtime.Effects.Apply(entity, PersistentCue);
		cues.BindEffects(runtime.Effects);

		Assert.Equal(1, cues.ActiveCueCount);

		cues.Dispose();

		// The effect system must not call into a disposed cue system.
		Assert.True(runtime.Effects.RemoveAll(entity) > 0);
		Assert.Equal(0, runtime.Effects.ActiveEffectCount);
	}
}
