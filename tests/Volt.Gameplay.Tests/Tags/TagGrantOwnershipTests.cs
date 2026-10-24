using Volt.Ecs;
using Volt.Gameplay.Abilities;
using Volt.Gameplay.Cues;
using Volt.Gameplay.Effects;
using Volt.Gameplay.Tags;
using Volt.Gameplay.Tasks;
using Volt.Gameplay.Tests.Abilities;
using Volt.Gameplay.Tests.Effects;
using Volt.Gameplay.Tests.Tasks;

namespace Volt.Gameplay.Tests.Tags;

/// <summary>
/// Covers the ownership contract that keeps bulk clearing and direct
/// removal from corrupting an effect's tag bookkeeping.
/// </summary>
/// <remarks>
/// <para>
/// A grant recorded by <see cref="GameplayTagStore.AddTag"/> is
/// unowned; a grant recorded by an effect is owned by that instance.
/// Both contribute to the same visible count, and each release is
/// scoped to the pool it can attribute. That is what makes the
/// dangerous sequence impossible: an effect whose tag an external
/// caller removed would, on expiry, decrement whatever grant was
/// current and silently delete somebody else's tag.
/// </para>
/// <para>
/// Every test here is a regression for that sequence or for one of its
/// reentrant variants. Counts are asserted exactly rather than as
/// booleans, because the defect leaves a plausible-looking boolean
/// behind and only the counter reveals it.
/// </para>
/// </remarks>
public sealed class TagGrantOwnershipTests
{
	[Fact]
	public void Clear_KeepsTheGrantOfOneActiveEffect()
	{
		using EffectRuntime runtime = new();
		Entity entity = runtime.CreateAffectedEntity();

		runtime.Effects.Apply(entity, EffectSchema.Stun);
		runtime.Tags.AddTag(entity, TagSchema.StatusBurning);

		Assert.True(runtime.Tags.Clear(entity));

		Assert.Equal(1, runtime.Tags.GetTagCount(entity, TagSchema.StatusStunned));
		Assert.Equal(0, runtime.Tags.GetTagCount(entity, TagSchema.StatusBurning));
		Assert.True(runtime.Tags.HasTag(entity, TagSchema.Status));
		Assert.Equal(1, runtime.Effects.ActiveEffectCount);
		Assert.False(runtime.Tags.Clear(entity));
	}

	[Fact]
	public void Clear_KeepsEveryGrantOfSeveralActiveEffects()
	{
		using EffectRuntime runtime = new();
		Entity entity = runtime.CreateAffectedEntity();

		runtime.Effects.Apply(entity, EffectSchema.Stun);
		runtime.Effects.Apply(entity, EffectSchema.BuffFiveSeconds);
		runtime.Tags.AddTag(entity, TagSchema.StatusBurning);
		runtime.Tags.AddTag(entity, TagSchema.OtherLeaf);

		Assert.True(runtime.Tags.Clear(entity));

		Assert.Equal(1, runtime.Tags.GetTagCount(entity, TagSchema.StatusStunned));
		Assert.Equal(1, runtime.Tags.GetTagCount(entity, TagSchema.EffectBuff));
		Assert.Equal(0, runtime.Tags.GetTagCount(entity, TagSchema.StatusBurning));
		Assert.Equal(0, runtime.Tags.GetTagCount(entity, TagSchema.OtherLeaf));
		Assert.Equal(2, runtime.Effects.ActiveEffectCount);
	}

	[Fact]
	public void Clear_TagSharedByTwoEffects_EachExpiryReleasesExactlyOneGrant()
	{
		using EffectRuntime runtime = new();
		Entity entity = runtime.CreateAffectedEntity();

		// Two distinct instances own the same tag.
		runtime.Effects.Apply(entity, SharedGrant("A"));
		runtime.Effects.Apply(entity, SharedGrant("B"));

		Assert.Equal(2, runtime.Tags.GetTagCount(entity, TagSchema.EffectBuff));

		runtime.Tags.Clear(entity);

		Assert.Equal(2, runtime.Tags.GetTagCount(entity, TagSchema.EffectBuff));

		Assert.True(runtime.Effects.RemoveAll(entity) > 0);
		Assert.Equal(0, runtime.Tags.GetTagCount(entity, TagSchema.EffectBuff));
		Assert.Equal(0, runtime.Tags.TaggedEntityCount);
	}

	[Fact]
	public void Clear_ThenExternalRegrant_ExpiringTheEffectDoesNotStealTheRegrant()
	{
		using EffectRuntime runtime = new();
		Entity entity = runtime.CreateAffectedEntity();

		runtime.Effects.Apply(entity, EffectSchema.Stun);

		// The dangerous shape the old contract allowed: an external
		// grant of a tag the effect also grants.
		runtime.Tags.Clear(entity);
		runtime.Tags.AddTag(entity, TagSchema.StatusStunned);

		Assert.Equal(2, runtime.Tags.GetTagCount(entity, TagSchema.StatusStunned));

		Assert.Equal(1, runtime.Effects.RemoveAll(entity));
		Assert.Equal(1, runtime.Tags.GetTagCount(entity, TagSchema.StatusStunned));
		Assert.True(runtime.Tags.RemoveTag(entity, TagSchema.StatusStunned));
		Assert.Equal(0, runtime.Tags.GetTagCount(entity, TagSchema.StatusStunned));
	}

	[Fact]
	public void RemoveTag_SharedByEffectAndExternal_RemovesOnlyTheExternalGrant()
	{
		using EffectRuntime runtime = new();
		Entity entity = runtime.CreateAffectedEntity();

		runtime.Effects.Apply(entity, EffectSchema.Stun);
		runtime.Tags.AddTag(entity, TagSchema.StatusStunned);

		Assert.True(runtime.Tags.RemoveTag(entity, TagSchema.StatusStunned));
		Assert.Equal(1, runtime.Tags.GetTagCount(entity, TagSchema.StatusStunned));

		// The only grant left belongs to the effect, so an external
		// removal must report that it removed nothing.
		Assert.False(runtime.Tags.RemoveTag(entity, TagSchema.StatusStunned));
		Assert.Equal(1, runtime.Tags.GetTagCount(entity, TagSchema.StatusStunned));

		runtime.Effects.RemoveAll(entity);

		Assert.Equal(0, runtime.Tags.GetTagCount(entity, TagSchema.StatusStunned));
		Assert.Equal(0, runtime.Tags.TaggedEntityCount);
	}

	[Fact]
	public void Clear_DuringAnEffectNotification_LeavesExactCounts()
	{
		using EffectRuntime runtime = new();
		Entity entity = runtime.CreateAffectedEntity();
		bool cleared = false;

		runtime.Tags.AddTag(entity, TagSchema.StatusBurning);

		runtime.Effects.Changed += change =>
		{
			if (cleared || change.Kind != GameplayEffectChangeKind.Applied)
			{
				return;
			}

			cleared = true;

			// Reentrant bulk clear while the applied effect is being
			// reported: its grant already exists and must survive.
			Assert.True(runtime.Tags.Clear(entity));
			Assert.Equal(
				1,
				runtime.Tags.GetTagCount(entity, TagSchema.StatusStunned));
			Assert.Equal(
				0,
				runtime.Tags.GetTagCount(entity, TagSchema.StatusBurning));
		};

		runtime.Effects.Apply(entity, EffectSchema.Stun);

		Assert.True(cleared);
		Assert.Equal(1, runtime.Tags.GetTagCount(entity, TagSchema.StatusStunned));

		runtime.Effects.RemoveAll(entity);

		Assert.Equal(0, runtime.Tags.GetTagCount(entity, TagSchema.StatusStunned));
	}

	[Fact]
	public void Clear_RemovesTheTagAnActivationRequirementReads()
	{
		using AbilityRuntime runtime = new();
		Entity actor = runtime.CreateActor();

		Assert.True(runtime.Tags.AddTag(actor, TagSchema.StatusStunned));

		GameplayAbilityHandle handle = runtime.Abilities.GiveAbility(
			actor,
			AbilitySchema.RequiresStatus);

		Assert.Equal(
			GameplayAbilityActivationStatus.ActivatedAndEnded,
			runtime.Abilities.TryActivate(handle).Status);

		Assert.True(runtime.Tags.Clear(actor));
		Assert.Equal(
			GameplayAbilityActivationStatus.RejectedByRequirements,
			runtime.Abilities.TryActivate(handle).Status);
	}

	[Fact]
	public void Clear_CompletesATaskWaitingForTheTagToDisappear()
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
			GameplayTagWaitMode.Absent);

		Assert.False(task.IsNone);

		runtime.Tasks.Advance(0.5f);

		Assert.Equal(
			0,
			recorder.Count(GameplayAbilityTaskChangeKind.Completed));

		Assert.True(runtime.Tags.Clear(actor));
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
	public void Clear_LeavesAPersistentEffectCueUntilTheEffectEnds()
	{
		GameplayEffectDefinition definition = GameplayEffectDefinition
			.Create("Test.OwnershipCue")
			.Infinite()
			.GrantTags(TagSchema.StatusStunned)
			.GameplayCue(TagSchema.CueBurning)
			.Build();

		using AbilityRuntime runtime = new();
		using GameplayCueSystem cues = new(runtime.World);
		cues.BindEffects(runtime.Effects);

		List<GameplayCueKind> kinds = [];
		cues.Subscribe(TagSchema.CueBurning, change => kinds.Add(change.Kind));

		Entity entity = runtime.CreateActor();

		runtime.Effects.Apply(entity, definition);
		Assert.Equal(1, cues.ActiveCueCount);

		// Nothing on the entity is unowned, so the clear removes
		// nothing and reports that it changed no grants.
		Assert.False(runtime.Tags.Clear(entity));

		// Clearing unowned tags cannot change effect lifetime, so the
		// persistent cue and the owned grant both survive.
		Assert.Equal(1, cues.ActiveCueCount);
		Assert.Equal(1, runtime.Tags.GetTagCount(entity, TagSchema.StatusStunned));

		runtime.Effects.RemoveAll(entity);

		Assert.Equal(0, cues.ActiveCueCount);
		Assert.Equal(0, runtime.Tags.GetTagCount(entity, TagSchema.StatusStunned));
		Assert.Equal(
			[GameplayCueKind.Added, GameplayCueKind.Removed],
			kinds);
	}

	[Fact]
	public void Clear_WhileAnEffectIsInhibited_DoesNotResurrectItsGrant()
	{
		GameplayEffectDefinition definition = GameplayEffectDefinition
			.Create("Test.OwnershipInhibited")
			.Infinite()
			.GrantTags(TagSchema.StatusStunned)
			.RequireWhileActive(
				GameplayTagRequirements.Require(
					GameplayTagSet.Create(TagSchema.StatusBurning)))
			.Build();

		using EffectRuntime runtime = new();
		Entity entity = runtime.CreateAffectedEntity();

		runtime.Effects.Apply(entity, definition);

		Assert.True(runtime.Effects.TryFindEffect(
			entity,
			definition,
			out ActiveGameplayEffect applied));
		Assert.True(applied.IsInhibited);
		Assert.Equal(0, runtime.Tags.GetTagCount(entity, TagSchema.StatusStunned));
		Assert.False(runtime.Tags.Clear(entity));

		runtime.Tags.AddTag(entity, TagSchema.StatusBurning);

		Assert.True(runtime.Effects.TryFindEffect(
			entity,
			definition,
			out ActiveGameplayEffect resumed));
		Assert.False(
			resumed.IsInhibited,
			$"Effect stayed inhibited: {resumed}");
		Assert.Equal(1, runtime.Tags.GetTagCount(entity, TagSchema.StatusStunned));

		Assert.True(runtime.Tags.Clear(entity));

		// The burning grant was unowned and is gone. Losing it also
		// makes the effect's ongoing requirement fail, so the instance
		// becomes inhibited and withdraws its own grant through the
		// ordinary path rather than through the clear.
		Assert.Equal(0, runtime.Tags.GetTagCount(entity, TagSchema.StatusBurning));
		Assert.Equal(0, runtime.Tags.GetTagCount(entity, TagSchema.StatusStunned));
		Assert.True(runtime.Effects.TryFindEffect(
			entity,
			definition,
			out ActiveGameplayEffect reacquired));
		Assert.True(reacquired.IsInhibited);
		Assert.Equal(1, runtime.Effects.ActiveEffectCount);

		runtime.Effects.RemoveAll(entity);

		Assert.Equal(0, runtime.Tags.TaggedEntityCount);
		Assert.Equal(0, runtime.Effects.ActiveEffectCount);
	}

	[Fact]
	public void Clear_AcrossDestructionAndIndexReuse_LeavesNoStaleGrants()
	{
		using EffectRuntime runtime = new();
		Entity first = runtime.CreateAffectedEntity();

		runtime.Effects.Apply(first, EffectSchema.Stun);
		runtime.Tags.AddTag(first, TagSchema.StatusBurning);

		Assert.True(runtime.World.DestroyEntity(first));

		Entity replacement = runtime.CreateAffectedEntity();

		Assert.Equal(first.Index, replacement.Index);
		Assert.NotEqual(first.Generation, replacement.Generation);
		Assert.False(runtime.Tags.HasTag(replacement, TagSchema.StatusStunned));
		Assert.False(runtime.Tags.HasTag(replacement, TagSchema.StatusBurning));
		Assert.False(runtime.Tags.Clear(replacement));
		Assert.Equal(0, runtime.Tags.GetTagCount(replacement, TagSchema.StatusStunned));

		Assert.True(runtime.Tags.AddTag(replacement, TagSchema.StatusStunned));
		Assert.Equal(1, runtime.Tags.GetTagCount(replacement, TagSchema.StatusStunned));
		Assert.Equal(1, runtime.Tags.TaggedEntityCount);
	}

	[Fact]
	public void RepeatedApplyClearRemove_KeepsCountsExact()
	{
		using EffectRuntime runtime = new();
		Entity entity = runtime.CreateAffectedEntity();

		for (int iteration = 0; iteration < 200; iteration++)
		{
			runtime.Effects.Apply(entity, EffectSchema.Stun);
			runtime.Tags.AddTag(entity, TagSchema.StatusStunned);

			Assert.True(runtime.Tags.Clear(entity));
			Assert.Equal(
				1,
				runtime.Tags.GetTagCount(entity, TagSchema.StatusStunned));

			Assert.Equal(1, runtime.Effects.RemoveAll(entity));
			Assert.Equal(
				0,
				runtime.Tags.GetTagCount(entity, TagSchema.StatusStunned));
			Assert.Equal(0, runtime.Tags.TaggedEntityCount);
			Assert.Equal(0, runtime.Effects.ActiveEffectCount);
		}
	}

	private static GameplayEffectDefinition SharedGrant(string name)
	{
		return GameplayEffectDefinition
			.Create($"Test.OwnershipShared{name}")
			.Infinite()
			.GrantTags(TagSchema.EffectBuff)
			.Build();
	}

	[Fact]
	public void Clear_AttemptsEveryNotificationAndAggregatesFailures()
	{
		using World world = new();
		using GameplayTagStore store = new(world);
		Entity entity = world.CreateEntity();
		int tagNotifications = 0;
		List<string> notified = [];
		bool throwing = true;

		store.AddTag(entity, TagSchema.Status);
		store.AddTag(entity, TagSchema.Effect);
		store.AddTag(entity, TagSchema.OtherLeaf);

		store.TagChanged += (_, tag) =>
		{
			tagNotifications++;
			notified.Add(GameplayTagRegistry.Shared.GetName(tag));

			if (throwing)
			{
				throw new InvalidOperationException("tag listener failure");
			}
		};

		AggregateException failure = Assert.Throws<AggregateException>(
			() => store.Clear(entity));

		Assert.Equal(3, tagNotifications);
		Assert.Equal(3, failure.InnerExceptions.Count);
		Assert.Equal(
			[
				"VoltGameplayTests.Effect",
				"VoltGameplayTests.Status",
				"VoltGameplayTests.Other.Leaf"
			],
			notified);
		Assert.Equal(0, store.TaggedEntityCount);
		Assert.False(store.HasTag(entity, TagSchema.Status));

		// The guard is released even though the notifications threw.
		throwing = false;
		Assert.True(store.AddTag(entity, TagSchema.Status));
		Assert.True(store.Clear(entity));
	}
}
