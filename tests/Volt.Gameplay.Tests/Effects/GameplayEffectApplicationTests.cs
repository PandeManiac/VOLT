using Volt.Ecs;
using Volt.Gameplay.Attributes;
using Volt.Gameplay.Effects;
using Volt.Gameplay.Tags;
using Volt.Gameplay.Tests.Attributes;
using Volt.Gameplay.Tests.Tags;

namespace Volt.Gameplay.Tests.Effects;

public sealed class GameplayEffectApplicationTests
{
	[Fact]
	public void InstantEffect_ChangesTheBaseValueAndIsNotTracked()
	{
		using EffectRuntime runtime = new();
		Entity entity = runtime.CreateAffectedEntity();

		GameplayEffectApplyResult result =
			runtime.Effects.Apply(entity, EffectSchema.InstantMinusTen);

		Assert.Equal(GameplayEffectApplyStatus.InstantExecuted, result.Status);
		Assert.True(result.Applied);
		Assert.True(result.Handle.IsNone);
		Assert.Equal(90f, runtime.Attributes.GetBaseValue(
			entity,
			AttributeSchema.Unbounded));
		Assert.Equal(90f, runtime.Attributes.GetCurrentValue(
			entity,
			AttributeSchema.Unbounded));
		Assert.Equal(0, runtime.Effects.ActiveEffectCount);
		Assert.Equal(0, runtime.Effects.GetEffectCount(entity));
	}

	[Fact]
	public void InstantEffect_FoldsItsModifiersInDeclarationOrder()
	{
		using EffectRuntime runtime = new();
		Entity entity = runtime.CreateAffectedEntity();

		runtime.Effects.Apply(entity, EffectSchema.InstantScaleThenAdd);

		// (100 * 2) + 5, which proves the multiply ran before the add
		// and that each step reads the previous result.
		Assert.Equal(205f, runtime.Attributes.GetBaseValue(
			entity,
			AttributeSchema.Unbounded));
	}

	[Fact]
	public void InstantEffect_ChangesOnlyTheBaseValue()
	{
		using EffectRuntime runtime = new();
		Entity entity = runtime.CreateAffectedEntity();

		runtime.Attributes.AddModifier(
			entity,
			AttributeSchema.Unbounded,
			GameplayModifierOperation.AddBase,
			30f);

		runtime.Effects.Apply(entity, EffectSchema.InstantMinusTen);

		Assert.Equal(90f, runtime.Attributes.GetBaseValue(
			entity,
			AttributeSchema.Unbounded));
		Assert.Equal(120f, runtime.Attributes.GetCurrentValue(
			entity,
			AttributeSchema.Unbounded));
	}

	[Fact]
	public void InstantEffect_ClampsToTheAttributeRange()
	{
		using EffectRuntime runtime = new();
		Entity entity = runtime.CreateAffectedEntity();

		runtime.Effects.Apply(entity, EffectSchema.InstantHealTen);

		Assert.Equal(100f, runtime.Attributes.GetBaseValue(
			entity,
			AttributeSchema.Health));
		Assert.False(runtime.Attributes.HasAttribute(
			entity,
			AttributeSchema.Health));
	}

	[Fact]
	public void InstantEffect_DoesNotGrantTags()
	{
		using EffectRuntime runtime = new();
		Entity entity = runtime.CreateAffectedEntity();

		GameplayEffectDefinition instantStun = GameplayEffectDefinition
			.Create("Test.InstantStun")
			.Instant()
			.GrantTags(TagSchema.StatusStunned)
			.Build();

		runtime.Effects.Apply(entity, instantStun);

		Assert.False(runtime.Tags.HasTag(entity, TagSchema.StatusStunned));
	}

	[Fact]
	public void DurationEffect_HoldsATemporaryModifierAndItsTags()
	{
		using EffectRuntime runtime = new();
		Entity entity = runtime.CreateAffectedEntity();

		GameplayEffectApplyResult result =
			runtime.Effects.Apply(entity, EffectSchema.BuffFiveSeconds);

		Assert.Equal(GameplayEffectApplyStatus.Applied, result.Status);
		Assert.False(result.Handle.IsNone);
		Assert.Equal(100f, runtime.Attributes.GetBaseValue(
			entity,
			AttributeSchema.Unbounded));
		Assert.Equal(120f, runtime.Attributes.GetCurrentValue(
			entity,
			AttributeSchema.Unbounded));
		Assert.True(runtime.Tags.HasTag(entity, TagSchema.EffectBuff));
		Assert.Equal(1, runtime.Effects.GetEffectCount(entity));
		Assert.Equal(1, runtime.Effects.ActiveEffectCount);
	}

	[Fact]
	public void ApplyingTwice_WithoutStacking_CreatesTwoInstances()
	{
		using EffectRuntime runtime = new();
		Entity entity = runtime.CreateAffectedEntity();
		GameplayEffectDefinition buff = GameplayEffectDefinition
			.Create("Test.TwoInstances")
			.HasDuration(5f)
			.Modifier(
				AttributeSchema.Unbounded,
				GameplayModifierOperation.AddBase,
				10f)
			.GrantTags(TagSchema.EffectBuff)
			.Build();

		runtime.Effects.Apply(entity, buff);
		runtime.Effects.Apply(entity, buff);

		Assert.Equal(2, runtime.Effects.GetEffectCount(entity));
		Assert.Equal(120f, runtime.Attributes.GetCurrentValue(
			entity,
			AttributeSchema.Unbounded));
		Assert.Equal(2, runtime.Tags.GetTagCount(entity, TagSchema.EffectBuff));
	}

	[Fact]
	public void TwoEffectsGrantingTheSameTag_SurviveOneRemoval()
	{
		using EffectRuntime runtime = new();
		Entity entity = runtime.CreateAffectedEntity();

		GameplayEffectApplyResult first =
			runtime.Effects.Apply(entity, EffectSchema.Stun);

		GameplayEffectApplyResult second =
			runtime.Effects.Apply(entity, EffectSchema.Stun);

		Assert.True(runtime.Tags.HasTag(entity, TagSchema.StatusStunned));

		Assert.True(runtime.Effects.Remove(first.Handle));

		Assert.True(runtime.Tags.HasTag(entity, TagSchema.StatusStunned));

		Assert.True(runtime.Effects.Remove(second.Handle));

		Assert.False(runtime.Tags.HasTag(entity, TagSchema.StatusStunned));
		Assert.False(runtime.Tags.HasTag(entity, TagSchema.Status));
	}

	[Fact]
	public void InheritedTagRequirement_IsSatisfiedByAChildTag()
	{
		using EffectRuntime runtime = new();
		Entity entity = runtime.CreateAffectedEntity();

		GameplayEffectDefinition requiresStatus = GameplayEffectDefinition
			.Create("Test.RequiresStatus")
			.Infinite()
			.RequireToApply(
				GameplayTagRequirements.Require(
					GameplayTagSet.Create(TagSchema.Status)))
			.Build();

		Assert.Equal(
			GameplayEffectApplyStatus.RejectedByRequirements,
			runtime.Effects.Apply(entity, requiresStatus).Status);

		runtime.Effects.Apply(entity, EffectSchema.Stun);

		GameplayEffectApplyResult result =
			runtime.Effects.Apply(entity, requiresStatus);

		Assert.Equal(GameplayEffectApplyStatus.Applied, result.Status);
	}

	[Fact]
	public void ApplicationRequirements_RejectTheApplicationWithoutSideEffects()
	{
		using EffectRuntime runtime = new();
		Entity entity = runtime.CreateAffectedEntity();

		runtime.Effects.Apply(entity, EffectSchema.Stun);

		GameplayEffectApplyResult result =
			runtime.Effects.Apply(entity, EffectSchema.BlockedByStun);

		Assert.Equal(
			GameplayEffectApplyStatus.RejectedByRequirements,
			result.Status);
		Assert.False(result.Applied);
		Assert.True(result.Handle.IsNone);
		Assert.Equal(0, result.StackCount);
		Assert.Equal(100f, runtime.Attributes.GetBaseValue(
			entity,
			AttributeSchema.Unbounded));
	}

	[Fact]
	public void OngoingRequirements_InhibitUntilTheyAreMetAgain()
	{
		using EffectRuntime runtime = new();
		EffectRecorder recorder = new(runtime.Effects);
		Entity entity = runtime.CreateAffectedEntity();

		GameplayEffectApplyResult result =
			runtime.Effects.Apply(entity, EffectSchema.OnlyWhileBurning);

		Assert.Equal(GameplayEffectApplyStatus.Applied, result.Status);
		Assert.True(result.Handle.IsNone == false);

		Assert.True(runtime.Effects.TryGetEffect(
			result.Handle,
			out ActiveGameplayEffect applied));
		Assert.True(applied.IsInhibited);
		Assert.Equal(100f, runtime.Attributes.GetCurrentValue(
			entity,
			AttributeSchema.Unbounded));

		GameplayEffectHandle burn =
			runtime.Effects.Apply(entity, EffectSchema.BurnTenSeconds).Handle;

		Assert.True(runtime.Effects.TryGetEffect(
			result.Handle,
			out ActiveGameplayEffect active));
		Assert.False(active.IsInhibited);

		// The buff adds seven, and the burning effect already executed
		// its first tick for two.
		Assert.Equal(105f, runtime.Attributes.GetCurrentValue(
			entity,
			AttributeSchema.Unbounded));
		// The initial inhibited state was reported on the Applied
		// notification, so only the transition is counted here.
		Assert.Equal(0, recorder.Count(GameplayEffectChangeKind.Inhibited));
		Assert.Equal(1, recorder.Count(GameplayEffectChangeKind.Uninhibited));

		runtime.Effects.Remove(burn);

		Assert.True(runtime.Effects.TryGetEffect(
			result.Handle,
			out ActiveGameplayEffect inhibited));
		Assert.True(inhibited.IsInhibited);

		// The burn's first tick changed the permanent base value from
		// 100 to 98, and the withdrawing buff no longer adds seven.
		Assert.Equal(98f, runtime.Attributes.GetCurrentValue(
			entity,
			AttributeSchema.Unbounded));
		Assert.Equal(1, recorder.Count(GameplayEffectChangeKind.Inhibited));
	}

	[Fact]
	public void Inhibition_WithdrawsGrantedTags()
	{
		using EffectRuntime runtime = new();
		Entity entity = runtime.CreateAffectedEntity();

		GameplayEffectDefinition gated = GameplayEffectDefinition
			.Create("Test.GatedGrant")
			.Infinite()
			.GrantTags(TagSchema.EffectShield)
			.RequireWhileActive(
				GameplayTagRequirements.Require(
					GameplayTagSet.Create(TagSchema.StatusBurning)))
			.Build();

		runtime.Effects.Apply(entity, gated);

		Assert.False(runtime.Tags.HasTag(entity, TagSchema.EffectShield));

		GameplayEffectHandle burn =
			runtime.Effects.Apply(entity, EffectSchema.BurnTenSeconds).Handle;

		Assert.True(runtime.Tags.HasTag(entity, TagSchema.EffectShield));

		runtime.Effects.Remove(burn);

		Assert.False(runtime.Tags.HasTag(entity, TagSchema.EffectShield));
	}

	[Fact]
	public void RemovalRequirements_RemoveTheEffectWhenTheTagsAppear()
	{
		using EffectRuntime runtime = new();
		EffectRecorder recorder = new(runtime.Effects);
		Entity entity = runtime.CreateAffectedEntity();

		GameplayEffectApplyResult result =
			runtime.Effects.Apply(entity, EffectSchema.RemovedByStun);

		Assert.Equal(103f, runtime.Attributes.GetCurrentValue(
			entity,
			AttributeSchema.Unbounded));

		runtime.Effects.Apply(entity, EffectSchema.Stun);

		Assert.False(runtime.Effects.TryGetEffect(result.Handle, out _));
		Assert.Equal(100f, runtime.Attributes.GetCurrentValue(
			entity,
			AttributeSchema.Unbounded));
		Assert.Equal(1, recorder.Count(GameplayEffectChangeKind.Removed));
	}

	[Fact]
	public void Remove_IsIdempotentAndRejectsStaleHandles()
	{
		using EffectRuntime runtime = new();
		Entity entity = runtime.CreateAffectedEntity();

		GameplayEffectHandle handle =
			runtime.Effects.Apply(entity, EffectSchema.BuffFiveSeconds).Handle;

		Assert.True(runtime.Effects.Remove(handle));
		Assert.False(runtime.Effects.Remove(handle));

		GameplayEffectHandle reuse =
			runtime.Effects.Apply(entity, EffectSchema.BuffFiveSeconds).Handle;

		Assert.NotEqual(handle, reuse);
		Assert.False(runtime.Effects.Remove(handle));
		Assert.True(runtime.Effects.TryGetEffect(reuse, out _));
	}

	[Fact]
	public void DestroyingAnEntity_ReleasesItsEffectsAndTags()
	{
		using EffectRuntime runtime = new();
		EffectRecorder recorder = new(runtime.Effects);

		Entity entity = runtime.CreateAffectedEntity();

		runtime.Effects.Apply(entity, EffectSchema.BuffFiveSeconds);

		Assert.Equal(1, runtime.Effects.ActiveEffectCount);

		int beforeDestruction = recorder.Changes.Count;

		Assert.True(runtime.World.DestroyEntity(entity));

		Assert.Equal(0, runtime.Effects.ActiveEffectCount);
		Assert.Equal(0, runtime.Effects.GetEffectCount(entity));
		Assert.Equal(0, runtime.Tags.TaggedEntityCount);
		Assert.Equal(0, runtime.Tags.GetTagCount(entity, TagSchema.EffectBuff));

		// Destruction is silent: the state is gone for everyone.
		Assert.Equal(beforeDestruction, recorder.Changes.Count);
	}

	[Fact]
	public void RecycledEntityIndex_CannotSeeThePreviousOccupantsEffects()
	{
		using EffectRuntime runtime = new();
		Entity first = runtime.CreateAffectedEntity();

		runtime.Effects.Apply(first, EffectSchema.BuffFiveSeconds);

		Assert.True(runtime.World.DestroyEntity(first));

		Entity reused = runtime.World.CreateEntity();

		Assert.Equal(first.Index, reused.Index);
		Assert.NotEqual(first.Generation, reused.Generation);
		Assert.Equal(0, runtime.Effects.GetEffectCount(reused));
		Assert.False(runtime.Tags.HasTag(reused, TagSchema.EffectBuff));
		Assert.Equal(100f, runtime.Attributes.GetCurrentValue(
			reused,
			AttributeSchema.Unbounded));
	}

	[Fact]
	public void HandlesFromAnotherSystem_AreRejected()
	{
		using EffectRuntime first = new();
		using EffectRuntime second = new();

		GameplayEffectHandle handle = first.Effects.Apply(
			first.CreateAffectedEntity(),
			EffectSchema.BuffFiveSeconds).Handle;

		Assert.False(second.Effects.Remove(handle));
		Assert.False(second.Effects.TryGetEffect(handle, out _));
	}

	[Fact]
	public void ApplyingToADeadEntity_Throws()
	{
		using EffectRuntime runtime = new();
		Entity entity = runtime.CreateAffectedEntity();

		Assert.True(runtime.World.DestroyEntity(entity));

		Assert.Throws<ArgumentException>(() =>
			runtime.Effects.Apply(entity, EffectSchema.BuffFiveSeconds));
	}

	[Fact]
	public void PerSourceStacking_MergesOnlyTheSameSource()
	{
		using EffectRuntime runtime = new();
		EffectRecorder recorder = new(runtime.Effects);

		Entity target = runtime.CreateAffectedEntity();
		Entity firstSource = runtime.CreateAffectedEntity();
		Entity secondSource = runtime.CreateAffectedEntity();

		GameplayEffectApplyResult first = runtime.Effects.Apply(
			target,
			EffectSchema.BleedPerSource,
			firstSource);

		GameplayEffectApplyResult second = runtime.Effects.Apply(
			target,
			EffectSchema.BleedPerSource,
			firstSource);

		Assert.Equal(GameplayEffectApplyStatus.Applied, first.Status);
		Assert.Equal(GameplayEffectApplyStatus.Stacked, second.Status);
		Assert.Equal(first.Handle, second.Handle);
		Assert.Equal(2, second.StackCount);
		Assert.Equal(1, runtime.Effects.GetEffectCount(target));

		GameplayEffectApplyResult third = runtime.Effects.Apply(
			target,
			EffectSchema.BleedPerSource,
			secondSource);

		Assert.Equal(GameplayEffectApplyStatus.Applied, third.Status);
		Assert.Equal(2, runtime.Effects.GetEffectCount(target));
		Assert.Equal(1, recorder.Count(GameplayEffectChangeKind.StackChanged));

		// Two stacks from the first source and one from the second.
		Assert.Equal(97f, runtime.Attributes.GetCurrentValue(
			target,
			AttributeSchema.Unbounded));
	}

	[Fact]
	public void PerTargetStacking_MergesAcrossSources()
	{
		using EffectRuntime runtime = new();

		Entity target = runtime.CreateAffectedEntity();
		Entity firstSource = runtime.CreateAffectedEntity();
		Entity secondSource = runtime.CreateAffectedEntity();

		runtime.Effects.Apply(
			target,
			EffectSchema.BleedFlatMagnitude,
			firstSource);

		GameplayEffectApplyResult second = runtime.Effects.Apply(
			target,
			EffectSchema.BleedFlatMagnitude,
			secondSource);

		Assert.Equal(GameplayEffectApplyStatus.Stacked, second.Status);
		Assert.Equal(2, second.StackCount);
		Assert.Equal(1, runtime.Effects.GetEffectCount(target));
		Assert.True(runtime.Effects.TryGetEffect(
			second.Handle,
			out ActiveGameplayEffect effect));
		Assert.Equal(secondSource, effect.Source);
	}

	[Fact]
	public void StackLimit_ClampsTheCountAndStillRefreshes()
	{
		using EffectRuntime runtime = new();
		EffectRecorder recorder = new(runtime.Effects);

		Entity target = runtime.CreateAffectedEntity();

		for (int index = 0; index < 8; index++)
		{
			runtime.Effects.Apply(target, EffectSchema.BleedPerSource);
		}

		Assert.True(runtime.Effects.TryFindEffect(
			target,
			EffectSchema.BleedPerSource,
			out ActiveGameplayEffect effect));

		Assert.Equal(5, effect.StackCount);
		Assert.Equal(95f, runtime.Attributes.GetCurrentValue(
			target,
			AttributeSchema.Unbounded));

		// Four applications changed the count; the rest re-applied at
		// the limit and reported no new stack.
		Assert.Equal(4, recorder.Count(GameplayEffectChangeKind.StackChanged));

		runtime.Effects.Advance(2f);

		Assert.True(runtime.Effects.TryFindEffect(
			target,
			EffectSchema.BleedPerSource,
			out ActiveGameplayEffect refreshed));

		// The last application refreshed the five second duration.
		Assert.Equal(3d, refreshed.RemainingDuration, 3);
	}

	[Fact]
	public void StackedMagnitudes_UseTheOperationBias()
	{
		using EffectRuntime runtime = new();
		Entity target = runtime.CreateAffectedEntity();

		runtime.Effects.Apply(target, EffectSchema.StackedMultiplier);
		runtime.Effects.Apply(target, EffectSchema.StackedMultiplier);

		// Two 1.5 additive multipliers sum to 2.0, not 2.25.
		Assert.Equal(200f, runtime.Attributes.GetCurrentValue(
			target,
			AttributeSchema.Unbounded));
	}

	[Fact]
	public void StackedCompoundMagnitude_IsRaisedToTheStackCount()
	{
		using EffectRuntime runtime = new();
		Entity target = runtime.CreateAffectedEntity();

		runtime.Effects.Apply(target, EffectSchema.StackedCompound);
		runtime.Effects.Apply(target, EffectSchema.StackedCompound);

		// 1.5^2 = 2.25.
		Assert.Equal(225f, runtime.Attributes.GetCurrentValue(
			target,
			AttributeSchema.Unbounded));
	}

	[Fact]
	public void FactorInStackCount_Off_KeepsOneMagnitude()
	{
		using EffectRuntime runtime = new();
		Entity target = runtime.CreateAffectedEntity();

		runtime.Effects.Apply(target, EffectSchema.BleedFlatMagnitude);
		runtime.Effects.Apply(target, EffectSchema.BleedFlatMagnitude);
		runtime.Effects.Apply(target, EffectSchema.BleedFlatMagnitude);

		Assert.Equal(99f, runtime.Attributes.GetCurrentValue(
			target,
			AttributeSchema.Unbounded));

		Assert.True(runtime.Effects.TryFindEffect(
			target,
			EffectSchema.BleedFlatMagnitude,
			out ActiveGameplayEffect effect));

		Assert.Equal(3, effect.StackCount);
	}

	[Fact]
	public void CopyEffectsAndFindEffect_ReportTheActiveInstances()
	{
		using EffectRuntime runtime = new();
		Entity target = runtime.CreateAffectedEntity();

		runtime.Effects.Apply(target, EffectSchema.Stun);
		runtime.Effects.Apply(target, EffectSchema.BuffFiveSeconds);

		Span<GameplayEffectHandle> handles = stackalloc GameplayEffectHandle[4];

		int count = runtime.Effects.CopyEffects(target, handles);

		Assert.Equal(2, count);
		Assert.Equal(2, runtime.Effects.GetEffectCount(target));
		Assert.True(runtime.Effects.TryGetEffect(handles[0], out _));
		Assert.True(runtime.Effects.TryGetEffect(handles[1], out _));

		Assert.True(runtime.Effects.TryFindEffect(
			target,
			EffectSchema.Stun,
			out ActiveGameplayEffect stun));

		Assert.Equal(1, stun.StackCount);
		Assert.True(double.IsPositiveInfinity(stun.RemainingDuration));
		Assert.True(double.IsPositiveInfinity(stun.RemainingPeriod));
		Assert.False(runtime.Effects.TryFindEffect(
			target,
			EffectSchema.BurnTenSeconds,
			out _));
	}

	[Fact]
	public void RemoveAll_RemovesEveryInstanceOnTheEntity()
	{
		using EffectRuntime runtime = new();
		Entity target = runtime.CreateAffectedEntity();

		runtime.Effects.Apply(target, EffectSchema.Stun);
		runtime.Effects.Apply(target, EffectSchema.BuffFiveSeconds);

		Assert.Equal(2, runtime.Effects.RemoveAll(target));
		Assert.Equal(0, runtime.Effects.ActiveEffectCount);
		Assert.Equal(0, runtime.Effects.RemoveAll(target));
		Assert.Equal(100f, runtime.Attributes.GetCurrentValue(
			target,
			AttributeSchema.Unbounded));
	}

	[Fact]
	public void HandlerMayApplyAnEffect_WhileAnotherIsBeingApplied()
	{
		using EffectRuntime runtime = new();
		Entity target = runtime.CreateAffectedEntity();

		GameplayEffectDefinition chained = GameplayEffectDefinition
			.Create("Test.Chained")
			.HasDuration(1f)
			.Modifier(
				AttributeSchema.Unbounded,
				GameplayModifierOperation.AddBase,
				1f)
			.Build();

		bool applied = false;

		runtime.Effects.Changed += change =>
		{
			if (applied || change.Kind != GameplayEffectChangeKind.Applied)
			{
				return;
			}

			applied = true;

			runtime.Effects.Apply(target, chained);
		};

		runtime.Effects.Apply(target, EffectSchema.BuffFiveSeconds);

		Assert.True(applied);
		Assert.Equal(2, runtime.Effects.GetEffectCount(target));
		Assert.Equal(121f, runtime.Attributes.GetCurrentValue(
			target,
			AttributeSchema.Unbounded));
	}

	[Fact]
	public void HandlerMayDestroyTheEntity_DuringApplication()
	{
		using EffectRuntime runtime = new();
		Entity target = runtime.CreateAffectedEntity();

		runtime.Attributes.AttributeChanged += change =>
		{
			if (runtime.World.IsAlive(change.Entity))
			{
				runtime.World.DestroyEntity(change.Entity);
			}
		};

		GameplayEffectApplyResult result =
			runtime.Effects.Apply(target, EffectSchema.BuffFiveSeconds);

		Assert.Equal(GameplayEffectApplyStatus.Discarded, result.Status);
		Assert.Equal(0, runtime.Effects.ActiveEffectCount);
		Assert.Equal(0, runtime.Tags.TaggedEntityCount);
	}

	[Fact]
	public void DisposedSystem_RejectsUse()
	{
		EffectRuntime runtime = new();
		Entity target = runtime.CreateAffectedEntity();

		runtime.Effects.Dispose();

		Assert.Throws<ObjectDisposedException>(() =>
			runtime.Effects.Apply(target, EffectSchema.Stun));

		runtime.Effects.Dispose();

		runtime.Tags.Dispose();
		runtime.Attributes.Dispose();
		runtime.World.Dispose();
	}
}
