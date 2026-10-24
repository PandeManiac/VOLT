using Volt.Ecs;
using Volt.Gameplay.Abilities;
using Volt.Gameplay.Attributes;
using Volt.Gameplay.Cues;
using Volt.Gameplay.Effects;
using Volt.Gameplay.Tags;
using Volt.Gameplay.Tests.Abilities;
using Volt.Gameplay.Tests.Attributes;
using Volt.Gameplay.Tests.Effects;
using Volt.Gameplay.Tests.Tags;

namespace Volt.Gameplay.Tests.Cues;

public sealed class GameplayCueSystemTests
{
	[Fact]
	public void Routing_CapturesListenersAndSkipsRemovedOrNewListenersDuringDispatch()
	{
		using World world = new();
		using GameplayCueSystem cues = new(world);
		GameplayCueSubscription victim = default;
		List<GameplayCueKind> order = [];
		victim = cues.Subscribe(TagSchema.CueBurning, change => order.Add(change.Kind));
		cues.Subscribe(TagSchema.Cue, _ =>
		{
			cues.Unsubscribe(victim);
			cues.Subscribe(TagSchema.CueBurning, change => order.Add(change.Kind));
		});

		cues.Execute(TagSchema.CueBurning);

		Assert.Equal([GameplayCueKind.Execute], order);
		Assert.Equal(2, cues.SubscriptionCount);
	}

	[Fact]
	public void PersistentCue_UpdatesAndRemovesExactlyOnceAcrossSlotReuse()
	{
		using World world = new();
		using GameplayCueSystem cues = new(world);
		Entity target = world.CreateEntity();
		List<GameplayCueEvent> changes = [];
		cues.Changed += changes.Add;
		GameplayCueHandle old = cues.Add(TagSchema.CueBurning, target, target, stackCount: 1);
		Assert.True(cues.Update(old, 2.5f, 3));
		Assert.True(cues.Remove(old));
		GameplayCueHandle replacement = cues.Add(TagSchema.CueBurning, target, target);
		Assert.Equal(old.Slot, replacement.Slot);
		Assert.NotEqual(old.Generation, replacement.Generation);
		Assert.False(cues.Remove(old));
		Assert.Equal(1, cues.ActiveCueCount);
		Assert.Equal([GameplayCueKind.Added, GameplayCueKind.Updated, GameplayCueKind.Removed, GameplayCueKind.Added], changes.Select(change => change.Kind));
		Assert.Equal(3, changes[1].StackCount);
		Assert.Equal(2.5f, changes[1].Magnitude);
	}

	[Fact]
	public void CrossSystemHandlesAndEntityDestructionAreSafe()
	{
		using World world = new();
		using GameplayCueSystem first = new(world);
		using GameplayCueSystem second = new(world);
		Entity target = world.CreateEntity();
		GameplayCueHandle old = first.Add(TagSchema.CueBurning, target, target);
		GameplayCueHandle replacement = second.Add(TagSchema.CueBurning, target, target);
		Assert.False(second.Remove(old));
		int removed = 0;
		first.Changed += change => { if (change.Kind == GameplayCueKind.Removed) { removed++; } };
		Assert.True(world.DestroyEntity(target));
		Assert.Equal(1, removed);
		Assert.Equal(0, first.ActiveCueCount);
		Assert.False(first.Remove(old));
		Assert.Equal(0, second.ActiveCueCount);
		Assert.False(second.Remove(replacement));
	}

	[Fact]
	public void BufferPreservesEventsAcrossMultipleStepsAndRejectsOverflow()
	{
		using World world = new();
		using GameplayCueSystem cues = new(world);
		using GameplayCueBuffer buffer = new(1, 16);
		cues.PresentationBuffer = buffer;
		cues.Execute(TagSchema.CueBurning);
		GameplayCueHandle handle = cues.Add(TagSchema.CueBurning);
		Assert.True(cues.Remove(handle));
		for (int index = 0; index < 3; index++)
		{
			cues.Execute(TagSchema.CueBurning);
		}

		Assert.Equal(6, buffer.Count);
		Span<GameplayCueEvent> events = stackalloc GameplayCueEvent[6];
		Assert.Equal(6, buffer.Drain(events));
		Assert.Equal(GameplayCueKind.Execute, events[0].Kind);
		Assert.Equal(GameplayCueKind.Added, events[1].Kind);
		Assert.Equal(GameplayCueKind.Removed, events[2].Kind);
		Assert.Equal(0, buffer.Count);
		GameplayCueHandle overflowCue = cues.Add(TagSchema.CueBurning);
		Assert.True(cues.Remove(overflowCue));
		using GameplayCueBuffer overflow = new(1, 2);
		Assert.Throws<InvalidOperationException>(() =>
		{
			overflow.Enqueue(new GameplayCueEvent(GameplayCueKind.Execute, TagSchema.CueBurning, default, default, default, default, 1f, 0, default, default));
			overflow.Enqueue(new GameplayCueEvent(GameplayCueKind.Execute, TagSchema.CueBurning, default, default, default, default, 1f, 0, default, default));
			overflow.Enqueue(new GameplayCueEvent(GameplayCueKind.Execute, TagSchema.CueBurning, default, default, default, default, 1f, 0, default, default));
		});
		Assert.Equal(2, overflow.Count);

		GameplayCueEvent[] wrappedExpected =
		[
			new(GameplayCueKind.Execute, TagSchema.CueBurning, default, default, default, default, 1f, 0, default, default),
			new(GameplayCueKind.Execute, TagSchema.CueBurning, default, default, default, default, 1f, 0, default, default),
			new(GameplayCueKind.Added, TagSchema.CueBurning, default, default, default, default, 1f, 1, default, default)
		];
		using GameplayCueBuffer wrapped = new(3, 6);
		wrapped.Enqueue(wrappedExpected[0]);
		wrapped.Enqueue(wrappedExpected[1]);
		Span<GameplayCueEvent> partial = stackalloc GameplayCueEvent[1];
		Assert.Equal(1, wrapped.Drain(partial));
		wrapped.Enqueue(wrappedExpected[2]);
		Span<GameplayCueEvent> rest = stackalloc GameplayCueEvent[2];
		Assert.Equal(2, wrapped.Drain(rest));
		Assert.Equal(wrappedExpected[1].Kind, rest[0].Kind);
		Assert.Equal(wrappedExpected[2].Kind, rest[1].Kind);

		GameplayCueBuffer disposed = new(1, 1);
		disposed.Dispose();
		Assert.Throws<ObjectDisposedException>(() => disposed.Enqueue(default));
		Assert.Throws<ObjectDisposedException>(() => disposed.Clear());
		buffer.Clear();
		cues.PresentationBuffer = null;
	}

	[Fact]
	public void EffectCueFollowsStackInhibitionExpirationAndTargetDestruction()
	{
		using World world = new();
		using GameplayAttributeStore attributes = new(world);
		using GameplayTagStore tags = new(world);
		using GameplayEffectSystem effects = new(world, attributes, tags);
		using GameplayCueSystem cues = new(world);
		cues.BindEffects(effects);
		GameplayEffectDefinition burning = GameplayEffectDefinition.Create("Cue.Burning")
			.HasDuration(1f).Stacking(GameplayEffectStackingType.AggregateByTarget)
			.GameplayCue(TagSchema.CueBurning).Build();
		Entity target = world.CreateEntity();
		GameplayEffectHandle first = effects.Apply(target, burning).Handle;
		Assert.True(cues.TryFindEffectForTesting(first, out GameplayCueHandle cue));
		effects.Apply(target, burning);
		Assert.True(cues.TryGet(cue, out GameplayCueEvent updated));
		Assert.Equal(2, updated.StackCount);
		effects.Advance(1f);
		Assert.Equal(0, cues.ActiveCueCount);
		Assert.False(cues.Remove(cue));
	}

	[Fact]
	public void EffectCueIsSuspendedDuringInhibitionAndRestoredWithUpdatedStacks()
	{
		using World world = new();
		using GameplayAttributeStore attributes = new(world);
		using GameplayTagStore tags = new(world);
		using GameplayEffectSystem effects = new(world, attributes, tags);
		using GameplayCueSystem cues = new(world);
		cues.BindEffects(effects);
		GameplayTag blocker = GameplayTagRegistry.Shared.Register("VoltGameplayTests.Cue.Blocker");
		GameplayEffectDefinition shield = GameplayEffectDefinition.Create("Cue.Shield")
			.HasDuration(5f)
			.Stacking(GameplayEffectStackingType.AggregateByTarget)
			.RequireWhileActive(new GameplayTagRequirements(ignored: GameplayTagSet.Create(blocker)))
			.GameplayCue(TagSchema.CueBurning)
			.Build();
		Entity target = world.CreateEntity();
		GameplayEffectHandle handle = effects.Apply(target, shield).Handle;
		Assert.Equal(1, cues.ActiveCueCount);
		GameplayCueHandle first = default;
		Assert.True(cues.TryFindEffectForTesting(handle, out first));

		Assert.True(tags.AddTag(target, blocker));
		Assert.Equal(0, cues.ActiveCueCount);
		Assert.False(cues.TryGet(first, out _));
		GameplayEffectApplyResult stacked = effects.Apply(target, shield);
		Assert.Equal(2, stacked.StackCount);
		Assert.Equal(0, cues.ActiveCueCount);

		Assert.True(tags.RemoveTag(target, blocker));
		Assert.Equal(1, cues.ActiveCueCount);
		Assert.True(cues.TryFindEffectForTesting(handle, out GameplayCueHandle restored));
		Assert.True(cues.TryGet(restored, out GameplayCueEvent active));
		Assert.Equal(2, active.StackCount);
		Assert.Equal(2f, active.Magnitude);
	}

	[Fact]
	public void EffectRemovalDuringAddedNotification_DoesNotLeaveCueOrStaleOwnerLink()
	{
		using World world = new();
		using GameplayAttributeStore attributes = new(world);
		using GameplayTagStore tags = new(world);
		using GameplayEffectSystem effects = new(world, attributes, tags);
		using GameplayCueSystem cues = new(world);
		cues.BindEffects(effects);
		GameplayEffectDefinition effect = GameplayEffectDefinition.Create("Cue.Reentrant")
			.HasDuration(4f).GameplayCue(TagSchema.CueBurning).Build();
		Entity target = world.CreateEntity();
		bool removed = false;
		cues.Changed += change =>
		{
			if (change.Kind == GameplayCueKind.Added && !removed)
			{
				removed = true;
				Assert.True(effects.Remove(change.Effect));
			}
		};

		GameplayEffectApplyResult result = effects.Apply(target, effect);

		Assert.True(removed);
		Assert.Equal(0, effects.ActiveEffectCount);
		Assert.Equal(0, cues.ActiveCueCount);
		Assert.False(result.Handle.IsNone);
		Assert.False(effects.TryGetEffect(result.Handle, out _));
	}

	[Fact]
	public void EffectRemovalDuringAddedNotification_LeavesNoEffectOwnedCue()
	{
		using World world = new();
		using GameplayAttributeStore attributes = new(world);
		using GameplayTagStore tags = new(world);
		using GameplayEffectSystem effects = new(world, attributes, tags);
		using GameplayCueSystem cues = new(world);
		cues.BindEffects(effects);
		GameplayEffectDefinition effect = GameplayEffectDefinition.Create("Cue.Reentrant.Verify")
			.HasDuration(4f).GameplayCue(TagSchema.CueBurning).Build();
		Entity target = world.CreateEntity();
		bool removed = false;
		cues.Changed += change =>
		{
			if (change.Kind == GameplayCueKind.Added && !removed)
			{
				removed = true;
				Assert.True(effects.Remove(change.Effect));
			}
		};

		GameplayEffectApplyResult result = effects.Apply(target, effect);

		Assert.True(removed);
		Assert.Equal(0, effects.ActiveEffectCount);
		Assert.Equal(0, cues.ActiveCueCount);
		Assert.False(effects.TryGetEffect(result.Handle, out _));
	}

	[Fact]
	public void AbilityPlanCueIsReleasedOnCancellation()
	{
		using AbilityRuntime runtime = new();
		Entity owner = runtime.CreateActor();
		GameplayAbilityPlan plan = GameplayAbilityPlan.Create()
			.AddCue(TagSchema.CueBurning)
			.Wait(10f)
			.Build();
		GameplayAbilityDefinition ability = GameplayAbilityDefinition.Create("Cue.Charge")
			.EndOnActivate(false)
			.Plan(plan)
			.Build();
		GameplayAbilityHandle handle = runtime.Abilities.GiveAbility(owner, ability);
		Assert.True(runtime.Abilities.TryActivate(handle).Succeeded);
		Assert.Equal(1, runtime.Abilities.Cues.ActiveCueCount);
		Assert.True(runtime.Abilities.Cancel(handle));
		Assert.Equal(0, runtime.Abilities.Cues.ActiveCueCount);
	}

}
